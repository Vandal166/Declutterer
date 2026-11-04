using System;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// "What did watching it over time show?" Turns an <see cref="ObservationSummary"/> into evidence:
///  • observed in use      strong, demotes the item (state Alive, or Retired for items that kept coming back to life)
///  • weak signal          small, and it holds quiet evidence back for the weak cooldown
///  • completed quiet      bounded and expiring; the combiner decides whether it may count at all
///  • inconclusive         a small coverage penalty and no junk evidence
///  • pinned or snoozed    a veto: the user said "keep it"
/// An item with no summary is exactly what it was before observation existed.
/// </summary>
public sealed class ObservationRule : IAssessmentRule
{
    /// <summary>Shared with the activity source that writes it, so a move can be told from other weak signals.</summary>
    public const string MovedDetailPrefix = "moved or renamed";

    private readonly ObservationOptions _options;

    public ObservationRule(ObservationOptions? options = null)
    {
        _options = options ?? new ObservationOptions();
    }

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        var o = ctx.Observation;
        if (o is null)
            return;

        var now = ctx.NowUtc;

        if (o.Pinned)
        {
            sink.Add(ReasonCode.PinnedByUser, EvidenceGroup.Observation, -5.0, 0.9, "you pinned it: it is never suggested for deletion", hardKeep: true);
            return;
        }
        if (o.SnoozeUntilUtc is { } snooze && snooze > now)
        {
            sink.Add(ReasonCode.PinnedByUser, EvidenceGroup.Observation, -5.0, 0.9, $"you snoozed it until {snooze:yyyy-MM-dd}", hardKeep: true);
            return;
        }

        switch (o.State)
        {
            case WatchState.Alive:
            case WatchState.Retired:
                sink.Add(ReasonCode.ObservedActive, EvidenceGroup.Observation, _options.ActiveLlr, _options.ActiveRisk, ActiveDetail(o, now));
                return;

            case WatchState.Unobservable:
                sink.Add(ReasonCode.ObservationInconclusive, EvidenceGroup.Observation, 0, 0.10,
                    "watching it was given up: it could not be checked reliably enough");
                return;

            case WatchState.Resolved:
                return;
        }

        // Watching or QuietEligible from here on.
        bool weakRecent = o.LastActiveStrength == UseStrength.Weak && o.LastActiveUtc is { } at && (now - at).TotalDays < _options.WeakCooldownDays;
        if (weakRecent)
        {
            var moved = o.LastActiveDetail?.StartsWith(MovedDetailPrefix, StringComparison.Ordinal) == true;
            var days = Math.Max(0, (now - o.LastActiveUtc!.Value).TotalDays);
            sink.Add(moved ? ReasonCode.ObservedMoved : ReasonCode.ObservedActive, EvidenceGroup.Observation, _options.WeakLlr, _options.WeakRisk,
                $"weak signal {OwnershipRule.Ago(days)}: {o.LastActiveDetail ?? "touched"}; quiet evidence is held back for {_options.WeakCooldownDays} days");
        }

        if (o.InconclusiveAttempts > 0)
        {
            sink.Add(ReasonCode.ObservationInconclusive, EvidenceGroup.Observation, 0, Math.Min(0.2, 0.05 * o.InconclusiveAttempts),
                $"{o.InconclusiveAttempts} check-in(s) in a row could not be compared reliably");
        }

        if (o.State == WatchState.QuietEligible && o.ValidUntilUtc is { } valid && valid > now && !weakRecent &&
            o.CoveredDays >= o.RequiredDays && ctx.IsDirectory)
        {
            sink.Add(ReasonCode.ObservedInactive, EvidenceGroup.Observation, _options.QuietLlrCap, 0,
                $"untouched for {o.CoveredDays} covered days (of {o.RequiredDays}); valid until {valid:yyyy-MM-dd}", riskRelief: _options.MaxRiskRelief);
        }
    }

    private static string ActiveDetail(ObservationSummary o, DateTime now)
    {
        var what = string.IsNullOrWhiteSpace(o.LastActiveDetail) ? "activity was observed" : o.LastActiveDetail;
        var when = o.LastActiveUtc is { } at ? $" {OwnershipRule.Ago(Math.Max(0, (now - at).TotalDays))}" : string.Empty;
        var until = o.State == WatchState.Alive && o.AliveUntilUtc is { } u ? $"; counts until {u:yyyy-MM-dd}" : "; it kept coming back to life while being watched";
        return $"observed in use{when}: {what}{until}";
    }
}
