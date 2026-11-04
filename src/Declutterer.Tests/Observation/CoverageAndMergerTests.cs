using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Tests.Observation;

public class CoverageAndMergerTests
{
    private static readonly DateTime D0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ObservationOptions O = new();

    private static WatchEntry Entry(int required = 45) =>
        ObservationTestKit.Entry("/data/item", D0.AddHours(14), requiredDays: required) with { RequiredDays = required };

    // ── coverage ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThirtyCalendarDaysWithThreeHeartbeatDays_AreThreeCoveredDays()
    {
        var tracker = new CoverageTracker(new[] { D0.AddDays(10), D0.AddDays(20), D0.AddDays(30) });

        var c = tracker.Evaluate(Entry(), D0.AddDays(30).AddHours(9), O);

        Assert.Equal(3, c.CoveredDays);
        Assert.Equal(30, c.CalendarDays);
        Assert.False(c.PoorCoverage);
    }

    [Fact]
    public void TheQuietSinceDayItself_IsNotCovered_AndFutureDaysAreIgnored()
    {
        var tracker = new CoverageTracker(new[] { D0, D0.AddDays(1), D0.AddDays(2), D0.AddDays(9) });

        Assert.Equal(2, tracker.CoveredDaysAfter(D0.AddHours(14), D0.AddDays(2).AddHours(1)));
    }

    [Fact]
    public void SeveralHeartbeatsOnOneDay_CountOnce_WhateverTheTimeOfDay()
    {
        var tracker = new CoverageTracker(new[] { D0.AddDays(1).AddHours(1), D0.AddDays(1).AddHours(23), D0.AddDays(1) });

        Assert.Equal(1, tracker.CoveredDaysAfter(D0, D0.AddDays(5)));
    }

    [Fact]
    public void SnoozedDatesAreExcluded_WhilePreviouslyCoveredDaysArePreserved()
    {
        var tracker = new CoverageTracker(new[]
        {
            D0.AddDays(1), D0.AddDays(3), D0.AddDays(5), D0.AddDays(11), D0.AddDays(12),
        });
        var entry = Entry(required: 10) with
        {
            CoveredDays = 2,
            LastCheckInUtc = D0.AddDays(2),
            SnoozedAtUtc = D0.AddDays(2),
            SnoozeUntilUtc = D0.AddDays(10),
        };

        var coverage = tracker.Evaluate(entry, D0.AddDays(12), O);

        Assert.Equal(4, coverage.CoveredDays); // two before snooze, two after it
        Assert.Equal(3, coverage.CalendarDays); // snoozed dates do not exhaust the coverage grace period
    }

    [Fact]
    public void ARarelyUsedMachine_IsFlaggedAsPoorCoverage_OnceThePatienceIsUsedUp()
    {
        var tracker = new CoverageTracker(new[] { D0.AddDays(5), D0.AddDays(50) });
        var e = Entry(required: 10);

        Assert.False(tracker.Evaluate(e, D0.AddDays(29), O).PoorCoverage);  // 29 <= 3 x 10 calendar days: still patient
        Assert.True(tracker.Evaluate(e, D0.AddDays(60), O).PoorCoverage);   // 60 days, 2 covered of 10
        var enough = new CoverageTracker(Enumerable.Range(1, 12).Select(i => D0.AddDays(i * 5)));
        Assert.False(enough.Evaluate(e, D0.AddDays(60), O).PoorCoverage);   // plenty of covered days, however spread out
    }

    // ── merging ──────────────────────────────────────────────────────────────────────────────

    private static ActivityReport Strong(string detail = "changed", DateTime? at = null) =>
        new(ObservationSource.SnapshotDiff, UseStrength.Strong, ActivityKind.ContentChange, at ?? D0, detail);

    private static ActivityReport Weak(string detail = "weak", DateTime? at = null, bool adopt = false) =>
        new(ObservationSource.AccessTime, UseStrength.Weak, ActivityKind.AccessTime, at ?? D0, detail, AdoptAsBaseline: adopt);

    private static ActivityReport Unsure(string detail = "walk incomplete") =>
        new(ObservationSource.SnapshotDiff, UseStrength.None, ActivityKind.Incomplete, null, detail, Conclusive: false);

    private static readonly ActivityReport Quiet = ActivityReport.Nothing(ObservationSource.SnapshotDiff);
    private static readonly CoverageEvaluation Fine = new(5, 6, false);

    [Fact]
    public void NothingFound_IsQuiet_AndCarriesTheCoverageAndAllSources()
    {
        var r = ActivityMerger.Merge(new[] { Quiet, ActivityReport.Nothing(ObservationSource.UsageArtifact) }, Fine, ObservationSource.Heartbeat);

        Assert.Equal(CheckInVerdict.Quiet, r.Verdict);
        Assert.Equal(5, r.CoveredDays);
        Assert.Equal(ObservationSource.SnapshotDiff | ObservationSource.UsageArtifact | ObservationSource.Heartbeat, r.Sources);
    }

    [Fact]
    public void Strong_BeatsEverything_IncludingAnInconclusiveComparison()
    {
        var r = ActivityMerger.Merge(new[] { Unsure(), Weak(), Strong("opened today", D0.AddDays(3)) }, Fine, ObservationSource.None);

        Assert.Equal(CheckInVerdict.StrongUse, r.Verdict);
        Assert.Equal(D0.AddDays(3), r.ActivityUtc);
        Assert.Equal("opened today", r.Detail);
    }

    [Fact]
    public void Unknown_BeatsWeakAndQuiet_NeverQuiet()
    {
        var withWeak = ActivityMerger.Merge(new[] { Unsure(), Weak() }, Fine, ObservationSource.None);
        var alone = ActivityMerger.Merge(new[] { Unsure("no baseline") }, Fine, ObservationSource.None);

        Assert.Equal(CheckInVerdict.Inconclusive, withWeak.Verdict);
        Assert.Equal(CheckInVerdict.Inconclusive, alone.Verdict);
        Assert.Equal("no baseline", alone.Detail);
    }

    [Fact]
    public void PoorCoverage_IsInconclusive_EvenWhenEverySourceIsQuiet()
    {
        var r = ActivityMerger.Merge(new[] { Quiet }, new CoverageEvaluation(2, 200, true), ObservationSource.None);

        Assert.Equal(CheckInVerdict.Inconclusive, r.Verdict);
        Assert.Contains("coverage too poor", r.Detail);
    }

    [Fact]
    public void Weak_BeatsQuiet_AndRemembersWhetherToAbsorbIt()
    {
        var r = ActivityMerger.Merge(new[] { Quiet, Weak("a file was last accessed 2 days ago", adopt: true) }, Fine, ObservationSource.None);

        Assert.Equal(CheckInVerdict.WeakUse, r.Verdict);
        Assert.True(r.AdoptAsBaseline);
    }

    [Fact]
    public void Missing_BeatsEverything()
    {
        var gone = new ActivityReport(ObservationSource.SnapshotDiff, UseStrength.None, ActivityKind.Missing, null, "no longer exists", IsMissing: true);

        Assert.Equal(CheckInVerdict.Missing, ActivityMerger.Merge(new[] { gone, Strong() }, Fine, ObservationSource.None).Verdict);
    }

    [Fact]
    public void TheNewLocationOfARenamedItem_SurvivesEveryVerdict()
    {
        var moved = Unsure() with { NewKey = "K", NewDisplayPath = "/new", NewFileId = "9:9" };

        var r = ActivityMerger.Merge(new[] { moved }, Fine, ObservationSource.None);

        Assert.Equal(CheckInVerdict.Inconclusive, r.Verdict);
        Assert.Equal("K", r.NewKey);
        Assert.Equal("/new", r.NewDisplayPath);
        Assert.Equal("9:9", r.NewFileId);
    }

    [Fact]
    public void TheFingerprintAndChanges_AreTakenFromTheReportThatHasThem()
    {
        var fp = ObservationTestKit.Print("cur");
        var change = new ChangeReport(true, false, false, 1, 2, 3);

        var r = ActivityMerger.Merge(new[] { ActivityReport.Nothing(ObservationSource.UsageArtifact), Strong() with { Fingerprint = fp, Changes = change } }, Fine, ObservationSource.None);

        Assert.Same(fp, r.CurrentFingerprint);
        Assert.Same(change, r.Changes);
    }
}
