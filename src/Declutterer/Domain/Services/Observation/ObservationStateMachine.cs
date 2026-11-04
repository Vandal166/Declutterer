using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>What one check-in concluded about one item (the merged result of all activity sources).</summary>
public sealed record CheckInResult
{
    public required CheckInVerdict Verdict { get; init; }
    /// <summary>When the activity happened, when known (newest write time, the time something was opened ...).</summary>
    public DateTime? ActivityUtc { get; init; }
    public string? Detail { get; init; }
    /// <summary>Covered days after the quiet-since day, from the heartbeat table. Only meaningful for Quiet / WeakUse.</summary>
    public int CoveredDays { get; init; }
    public ObservationSource Sources { get; init; }
    /// <summary>What the item looks like now (null if it could not be walked).</summary>
    public Fingerprint? CurrentFingerprint { get; init; }
    /// <summary>Weak changes (shell droppings, a rename) are absorbed into the baseline so they are not reported again at every check-in.</summary>
    public bool AdoptAsBaseline { get; init; }
    public ChangeReport? Changes { get; init; }
    public string? NewKey { get; init; }
    public string? NewDisplayPath { get; init; }
    public string? NewFileId { get; init; }
}

public enum TransitionKind
{
    None,
    /// <summary>Same state, fields updated.</summary>
    Progressed,
    BecameAlive,
    ExtendedAlive,
    AliveCooldownEnded,
    Retired,
    BecameQuietEligible,
    QuietExpired,
    BecameUnobservable,
    Resolved,
}

public sealed record TransitionOutcome(WatchEntry Entry, TransitionKind Kind, bool NeedsReassessment);

/// <summary>
/// The lifecycle of a watch entry as a pure function: <c>Transition(entry, result, now, options)</c> returns the next entry.
/// Nothing here touches the file system, the database or the clock.
///
///   Watching        strong use -> Alive | enough covered quiet days -> QuietEligible | too many inconclusive -> Unobservable | gone -> Resolved(Missing)
///   Alive           strong use -> stays Alive (cooldown extended) | cooldown over -> Watching with a new baseline (Retired after MaxAliveResets resets)
///   QuietEligible   strong use -> Alive | validity over -> Watching with a new baseline
///   Unobservable / Retired / Resolved  inactive: never transition on their own
/// Unknown always means alive or inconclusive; nothing here can turn missing evidence into quiet evidence.
/// </summary>
public static class ObservationStateMachine
{
    public static TransitionOutcome Transition(WatchEntry entry, CheckInResult result, DateTime nowUtc, ObservationOptions options)
    {
        if (!entry.IsActive)
            return new TransitionOutcome(entry, TransitionKind.None, false);

        var snoozeEnded = entry.SnoozedAtUtc is not null && entry.SnoozeUntilUtc is { } snoozeUntil && nowUtc >= snoozeUntil;
        var next = entry with
        {
            LastCheckInUtc = nowUtc,
            Sources = entry.Sources | result.Sources,
            SnoozedAtUtc = snoozeEnded ? null : entry.SnoozedAtUtc,
            SnoozeUntilUtc = snoozeEnded ? null : entry.SnoozeUntilUtc,
            QuietSinceUtc = snoozeEnded ? nowUtc : entry.QuietSinceUtc,
        };
        if (result.NewKey is not null)
            next = next with { Key = result.NewKey, DisplayPath = result.NewDisplayPath ?? next.DisplayPath };
        if (result.NewFileId is not null)
            next = next with { FileId = result.NewFileId };
        if (result.CurrentFingerprint is { IsComplete: true } fingerprint)
            next = next with { LastFingerprint = fingerprint };

        // 1. The item is gone and could not be found again.
        if (result.Verdict == CheckInVerdict.Missing)
            return new TransitionOutcome(next with { State = WatchState.Resolved, Resolution = WatchResolution.Missing, ResolvedUtc = nowUtc }, TransitionKind.Resolved, false);

        // 2. Strong use always wins, whatever else is going on.
        if (result.Verdict == CheckInVerdict.StrongUse)
        {
            var wasAlive = entry.State == WatchState.Alive;
            return new TransitionOutcome(next with
            {
                State = WatchState.Alive,
                AliveUntilUtc = nowUtc.AddDays(options.AliveCooldownDays),
                ValidUntilUtc = null,
                CoveredDays = 0,
                InconclusiveStreak = 0,
                LastActiveUtc = result.ActivityUtc ?? nowUtc,
                LastActiveStrength = UseStrength.Strong,
                LastActiveDetail = result.Detail,
            }, wasAlive ? TransitionKind.ExtendedAlive : TransitionKind.BecameAlive, false);
        }

        // 3. Time-based transitions.
        if (entry.State == WatchState.Alive && entry.AliveUntilUtc is { } until && nowUtc >= until)
        {
            if (entry.AliveResets >= options.MaxAliveResets)
                return new TransitionOutcome(next with { State = WatchState.Retired, CoveredDays = 0, AliveUntilUtc = null }, TransitionKind.Retired, false);

            return new TransitionOutcome(Restart(next, nowUtc, result) with { AliveResets = entry.AliveResets + 1, AliveUntilUtc = null },
                TransitionKind.AliveCooldownEnded, false);
        }

        if (entry.State == WatchState.QuietEligible && entry.ValidUntilUtc is { } valid && nowUtc >= valid)
            return new TransitionOutcome(Restart(next, nowUtc, result) with { ValidUntilUtc = null }, TransitionKind.QuietExpired, false);

        // 4. Apply the verdict.
        switch (result.Verdict)
        {
            case CheckInVerdict.Inconclusive:
            {
                var streak = entry.InconclusiveStreak + 1;
                next = next with { InconclusiveStreak = streak };

                // no usable baseline yet: this walk becomes it, and the quiet clock starts now
                if (next.BaselineFingerprint is null && result.CurrentFingerprint is { IsComplete: true } current)
                    next = next with { BaselineFingerprint = current, QuietSinceUtc = nowUtc, CoveredDays = 0 };

                if (streak >= options.MaxInconclusiveAttempts && entry.State != WatchState.Alive)
                    return new TransitionOutcome(next with { State = WatchState.Unobservable, AliveUntilUtc = null, ValidUntilUtc = null }, TransitionKind.BecameUnobservable, false);

                return new TransitionOutcome(next, TransitionKind.Progressed, false);
            }

            default: // Quiet or WeakUse: the comparison was conclusive
            {
                next = next with { InconclusiveStreak = 0 };
                if (entry.State != WatchState.Alive)
                    next = next with { CoveredDays = result.CoveredDays };

                if (result.Verdict == CheckInVerdict.WeakUse)
                {
                    if (entry.State != WatchState.Alive) // an alive item already carries the stronger statement
                        next = next with { LastActiveUtc = result.ActivityUtc ?? nowUtc, LastActiveStrength = UseStrength.Weak, LastActiveDetail = result.Detail };
                    if (result.AdoptAsBaseline && result.CurrentFingerprint is { IsComplete: true } absorbed)
                        next = next with { BaselineFingerprint = absorbed };
                }

                if (next.State == WatchState.Watching && next.CoveredDays >= next.RequiredDays && !HasRecentWeakSignal(next, nowUtc, options))
                {
                    return new TransitionOutcome(next with
                    {
                        State = WatchState.QuietEligible,
                        ValidUntilUtc = nowUtc.AddDays(options.QuietValidityDays),
                    }, TransitionKind.BecameQuietEligible, true);
                }

                return new TransitionOutcome(next, TransitionKind.Progressed, false);
            }
        }
    }

    /// <summary>True if a weak signal is recent enough to hold back quiet evidence.</summary>
    public static bool HasRecentWeakSignal(WatchEntry entry, DateTime nowUtc, ObservationOptions options) =>
        entry.LastActiveStrength == UseStrength.Weak && entry.LastActiveUtc is { } at && (nowUtc - at).TotalDays < options.WeakCooldownDays;

    /// <summary>Back to Watching with a fresh baseline (the current walk, if it is complete) and a restarted quiet clock.</summary>
    private static WatchEntry Restart(WatchEntry entry, DateTime nowUtc, CheckInResult result) => entry with
    {
        State = WatchState.Watching,
        BaselineFingerprint = result.CurrentFingerprint is { IsComplete: true } fp ? fp : null,
        QuietSinceUtc = nowUtc,
        CoveredDays = 0,
        InconclusiveStreak = 0,
        ValidUntilUtc = null,
    };
}
