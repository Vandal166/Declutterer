using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Tests.Observation;

public class ObservationStateMachineTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ObservationOptions O = new();

    private static WatchEntry Entry(WatchState state = WatchState.Watching, int required = 10) =>
        ObservationTestKit.Entry("/data/item", T0, requiredDays: required, baseline: ObservationTestKit.Print(), state: state) with { RequiredDays = required };

    private static CheckInResult R(CheckInVerdict v, int covered = 0, Fingerprint? fp = null, DateTime? at = null, string? detail = null, bool adopt = false) =>
        new() { Verdict = v, CoveredDays = covered, CurrentFingerprint = fp, ActivityUtc = at, Detail = detail, AdoptAsBaseline = adopt };

    private static TransitionOutcome Step(WatchEntry e, CheckInResult r, double days = 1) =>
        ObservationStateMachine.Transition(e, r, T0.AddDays(days), O);

    // ── the table ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(WatchState.Watching)]
    [InlineData(WatchState.Alive)]
    [InlineData(WatchState.QuietEligible)]
    public void StrongUse_FromAnyActiveState_MakesTheItemAlive_AndRestartsTheQuietClock(WatchState from)
    {
        var e = Entry(from) with { CoveredDays = 7, ValidUntilUtc = T0.AddDays(5), AliveUntilUtc = from == WatchState.Alive ? T0.AddDays(3) : null };

        var o = Step(e, R(CheckInVerdict.StrongUse, at: T0.AddDays(1), detail: "3 created"), 2);

        Assert.Equal(WatchState.Alive, o.Entry.State);
        Assert.Equal(T0.AddDays(2 + O.AliveCooldownDays), o.Entry.AliveUntilUtc);
        Assert.Equal(0, o.Entry.CoveredDays);
        Assert.Null(o.Entry.ValidUntilUtc);
        Assert.Equal(UseStrength.Strong, o.Entry.LastActiveStrength);
        Assert.Equal("3 created", o.Entry.LastActiveDetail);
        Assert.Equal(T0.AddDays(1), o.Entry.LastActiveUtc);
        Assert.Equal(from == WatchState.Alive ? TransitionKind.ExtendedAlive : TransitionKind.BecameAlive, o.Kind);
        Assert.False(o.NeedsReassessment);
    }

    [Theory]
    [InlineData(WatchState.Watching)]
    [InlineData(WatchState.Alive)]
    [InlineData(WatchState.QuietEligible)]
    public void Missing_FromAnyActiveState_ResolvesTheEntry(WatchState from)
    {
        var o = Step(Entry(from), R(CheckInVerdict.Missing));

        Assert.Equal(WatchState.Resolved, o.Entry.State);
        Assert.Equal(WatchResolution.Missing, o.Entry.Resolution);
        Assert.Equal(T0.AddDays(1), o.Entry.ResolvedUtc);
    }

    [Theory]
    [InlineData(WatchState.Unobservable)]
    [InlineData(WatchState.Retired)]
    [InlineData(WatchState.Resolved)]
    public void InactiveStates_NeverTransition(WatchState state)
    {
        foreach (var verdict in Enum.GetValues<CheckInVerdict>())
        {
            var e = Entry(state);
            var o = Step(e, R(verdict));

            Assert.Equal(TransitionKind.None, o.Kind);
            Assert.Same(e, o.Entry);
        }
    }

    // ── watching ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void QuietCheckIns_AccumulateCoveredDays_AndCompleteTheWindow()
    {
        var e = Entry(required: 3);

        var o1 = Step(e, R(CheckInVerdict.Quiet, covered: 1), 1);
        var o2 = Step(o1.Entry, R(CheckInVerdict.Quiet, covered: 2), 2);
        var o3 = Step(o2.Entry, R(CheckInVerdict.Quiet, covered: 3), 3);

        Assert.Equal(WatchState.Watching, o2.Entry.State);
        Assert.Equal(2, o2.Entry.CoveredDays);
        Assert.Equal(WatchState.QuietEligible, o3.Entry.State);
        Assert.Equal(T0.AddDays(3 + O.QuietValidityDays), o3.Entry.ValidUntilUtc);
        Assert.Equal(TransitionKind.BecameQuietEligible, o3.Kind);
        Assert.True(o3.NeedsReassessment);
        Assert.False(o2.NeedsReassessment);
    }

    [Fact]
    public void AWeakSignal_DoesNotResetCoverage_ButHoldsBackCompletionForTheWeakCooldown()
    {
        var e = Entry(required: 3) with { CoveredDays = 2 };
        var fp = ObservationTestKit.Print("new");

        var weak = Step(e, R(CheckInVerdict.WeakUse, covered: 3, fp: fp, at: T0.AddDays(2), detail: "Thumbs.db", adopt: true), 2);

        Assert.Equal(WatchState.Watching, weak.Entry.State); // 3 of 3 covered days, but a weak signal is fresh
        Assert.Equal(3, weak.Entry.CoveredDays);
        Assert.Equal(UseStrength.Weak, weak.Entry.LastActiveStrength);
        Assert.Equal("new", weak.Entry.BaselineFingerprint!.RootHash); // absorbed, so it is not reported again

        var later = Step(weak.Entry, R(CheckInVerdict.Quiet, covered: 4), 2 + O.WeakCooldownDays + 0.5);

        Assert.Equal(WatchState.QuietEligible, later.Entry.State);
    }

    [Fact]
    public void AWeakSignalWithoutAdoption_KeepsTheOldBaseline()
    {
        var e = Entry();

        var o = Step(e, R(CheckInVerdict.WeakUse, covered: 1, fp: ObservationTestKit.Print("other"), at: T0, adopt: false));

        Assert.Equal("aa", o.Entry.BaselineFingerprint!.RootHash);
    }

    [Fact]
    public void Inconclusive_CountsUp_AndNeverAddsCoverage()
    {
        var e = Entry(required: 3) with { CoveredDays = 2 };

        var o = Step(e, R(CheckInVerdict.Inconclusive, covered: 99));

        Assert.Equal(1, o.Entry.InconclusiveStreak);
        Assert.Equal(2, o.Entry.CoveredDays);
        Assert.Equal(WatchState.Watching, o.Entry.State);
    }

    [Fact]
    public void ConsecutiveInconclusiveCheckIns_MakeTheItemUnobservable_AndAConclusiveOneResetsTheCount()
    {
        var e = Entry();
        for (int i = 1; i < O.MaxInconclusiveAttempts; i++)
            e = Step(e, R(CheckInVerdict.Inconclusive), i).Entry;
        Assert.Equal(WatchState.Watching, e.State);

        var reset = Step(e, R(CheckInVerdict.Quiet, covered: 1), 20);
        Assert.Equal(0, reset.Entry.InconclusiveStreak);

        for (int i = 1; i <= O.MaxInconclusiveAttempts; i++)
            e = Step(e, R(CheckInVerdict.Inconclusive), 30 + i).Entry;
        Assert.Equal(WatchState.Unobservable, e.State);
    }

    [Fact]
    public void InconclusiveLimit_DoesNotDiscardAnAliveState()
    {
        var alive = Entry(WatchState.Alive) with
        {
            AliveUntilUtc = T0.AddDays(20),
            InconclusiveStreak = O.MaxInconclusiveAttempts - 1,
        };

        var outcome = Step(alive, R(CheckInVerdict.Inconclusive));

        Assert.Equal(WatchState.Alive, outcome.Entry.State);
        Assert.Equal(O.MaxInconclusiveAttempts, outcome.Entry.InconclusiveStreak);
        Assert.Equal(alive.AliveUntilUtc, outcome.Entry.AliveUntilUtc);
        Assert.Equal(TransitionKind.Progressed, outcome.Kind);
    }

    [Fact]
    public void ALostBaseline_IsRetakenFromTheCurrentWalk_AndTheQuietClockRestarts()
    {
        var e = Entry() with { BaselineFingerprint = null, CoveredDays = 5 };

        var o = Step(e, R(CheckInVerdict.Inconclusive, fp: ObservationTestKit.Print("fresh")), 4);

        Assert.Equal("fresh", o.Entry.BaselineFingerprint!.RootHash);
        Assert.Equal(T0.AddDays(4), o.Entry.QuietSinceUtc);
        Assert.Equal(0, o.Entry.CoveredDays);
    }

    // ── alive ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AliveItem_StaysAlive_UntilTheCooldownEnds_AndQuietCheckInsDoNotAddCoverage()
    {
        var e = Step(Entry(), R(CheckInVerdict.StrongUse, at: T0)).Entry;

        var during = Step(e, R(CheckInVerdict.Quiet, covered: 9), 10);

        Assert.Equal(WatchState.Alive, during.Entry.State);
        Assert.Equal(0, during.Entry.CoveredDays);
    }

    [Fact]
    public void WhenTheCooldownEnds_TheItemGetsAFreshBaseline_AndTheResetIsCounted()
    {
        var alive = Step(Entry(), R(CheckInVerdict.StrongUse, at: T0)).Entry;
        var fresh = ObservationTestKit.Print("after-use");

        var o = Step(alive, R(CheckInVerdict.Quiet, fp: fresh), 1 + O.AliveCooldownDays);

        Assert.Equal(WatchState.Watching, o.Entry.State);
        Assert.Equal(TransitionKind.AliveCooldownEnded, o.Kind);
        Assert.Equal(1, o.Entry.AliveResets);
        Assert.Equal("after-use", o.Entry.BaselineFingerprint!.RootHash);
        Assert.Equal(T0.AddDays(1 + O.AliveCooldownDays), o.Entry.QuietSinceUtc);
        Assert.Equal(0, o.Entry.CoveredDays);
        Assert.Null(o.Entry.AliveUntilUtc);
    }

    [Fact]
    public void WhenTheCooldownEndsWithoutACompleteWalk_TheBaselineIsLeftEmptyForTheNextCheckIn()
    {
        var alive = Step(Entry(), R(CheckInVerdict.StrongUse, at: T0)).Entry;

        var o = Step(alive, R(CheckInVerdict.Inconclusive, fp: ObservationTestKit.Print("partial", complete: false)), 1 + O.AliveCooldownDays);

        Assert.Equal(WatchState.Watching, o.Entry.State);
        Assert.Null(o.Entry.BaselineFingerprint); // an incomplete walk is never a baseline
    }

    [Fact]
    public void AnItemKeptAliveTooOften_IsRetired()
    {
        var e = Entry();
        for (int cycle = 0; cycle < O.MaxAliveResets; cycle++)
        {
            e = Step(e, R(CheckInVerdict.StrongUse, at: T0), 100 * cycle).Entry;
            e = Step(e, R(CheckInVerdict.Quiet, fp: ObservationTestKit.Print("x")), 100 * cycle + O.AliveCooldownDays + 1).Entry;
            Assert.Equal(WatchState.Watching, e.State);
            Assert.Equal(cycle + 1, e.AliveResets);
        }

        e = Step(e, R(CheckInVerdict.StrongUse, at: T0), 1000).Entry;
        var retired = Step(e, R(CheckInVerdict.Quiet), 1000 + O.AliveCooldownDays + 1);

        Assert.Equal(WatchState.Retired, retired.Entry.State);
        Assert.Equal(TransitionKind.Retired, retired.Kind);
    }

    // ── quiet eligible ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void QuietEvidence_ExpiresIntoWatchingWithAFreshBaseline_WithoutCountingAsAReset()
    {
        var q = Entry(WatchState.QuietEligible) with { ValidUntilUtc = T0.AddDays(30), CoveredDays = 10 };

        var still = Step(q, R(CheckInVerdict.Quiet, covered: 11), 29);
        var expired = Step(q, R(CheckInVerdict.Quiet, covered: 40, fp: ObservationTestKit.Print("renewed")), 31);

        Assert.Equal(WatchState.QuietEligible, still.Entry.State);
        Assert.Equal(WatchState.Watching, expired.Entry.State);
        Assert.Equal(TransitionKind.QuietExpired, expired.Kind);
        Assert.Equal(0, expired.Entry.AliveResets);
        Assert.Equal(0, expired.Entry.CoveredDays);
        Assert.Null(expired.Entry.ValidUntilUtc);
        Assert.Equal("renewed", expired.Entry.BaselineFingerprint!.RootHash);
    }

    // ── identity ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARename_ReanchorsTheEntry_WhateverTheVerdict()
    {
        var e = Entry();
        var r = new CheckInResult
        {
            Verdict = CheckInVerdict.WeakUse, CoveredDays = 1, NewKey = ObservationKey.From("/data/renamed"), NewDisplayPath = "/data/renamed",
            NewFileId = "7:8", AdoptAsBaseline = true, CurrentFingerprint = ObservationTestKit.Print("aa"),
        };

        var o = Step(e, r);

        Assert.Equal("/data/renamed", o.Entry.DisplayPath);
        Assert.Equal(ObservationKey.From("/data/renamed"), o.Entry.Key);
        Assert.Equal("7:8", o.Entry.FileId);
    }

    [Fact]
    public void TheLastFingerprint_IsOnlyReplacedByACompleteOne()
    {
        var e = Entry() with { LastFingerprint = ObservationTestKit.Print("old") };

        var partial = Step(e, R(CheckInVerdict.Inconclusive, fp: ObservationTestKit.Print("partial", complete: false)));
        var complete = Step(e, R(CheckInVerdict.Quiet, covered: 1, fp: ObservationTestKit.Print("new")));

        Assert.Equal("old", partial.Entry.LastFingerprint!.RootHash);
        Assert.Equal("new", complete.Entry.LastFingerprint!.RootHash);
    }

    [Fact]
    public void Transition_IsPure_TheInputIsNeverMutated()
    {
        var e = Entry();
        var before = e with { };

        Step(e, R(CheckInVerdict.StrongUse, at: T0));

        Assert.Equal(before, e);
    }
}
