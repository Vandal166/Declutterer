using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

public enum ActivityKind
{
    None,
    ContentChange,
    ShellDroppings,
    RecentOpen,
    Moved,
    Replaced,
    AccessTime,
    Missing,
    /// <summary>Something changed that could not be classified. Always treated as strong.</summary>
    Unclassifiable,
    /// <summary>The metadata walk did not finish (budget or unreadable entries).</summary>
    Incomplete,
    NoBaseline,
    Error,
}

/// <summary>What one <see cref="Abstractions.IActivitySource"/> found out about one item.</summary>
public sealed record ActivityReport(
    ObservationSource Source,
    UseStrength Strength,
    ActivityKind Kind,
    DateTime? ActivityUtc,
    string? Detail,
    bool Conclusive = true,
    bool IsMissing = false,
    ChangeReport? Changes = null,
    Fingerprint? Fingerprint = null,
    bool AdoptAsBaseline = false,
    string? NewKey = null,
    string? NewDisplayPath = null,
    string? NewFileId = null)
{
    public static ActivityReport Nothing(ObservationSource source) => new(source, UseStrength.None, ActivityKind.None, null, null);
}

/// <summary>
/// Merges the reports of all sources of one check-in into the verdict. Strong beats everything; an inconclusive comparison
/// beats weak and quiet (so unknown can never be mistaken for quiet); weak beats quiet.
/// </summary>
public static class ActivityMerger
{
    public static CheckInResult Merge(IReadOnlyList<ActivityReport> reports, CoverageEvaluation coverage, ObservationSource heartbeat)
    {
        var sources = reports.Aggregate(heartbeat, (acc, r) => acc | r.Source);
        var snapshot = reports.FirstOrDefault(r => r.Fingerprint is not null || r.Changes is not null)
                       ?? reports.FirstOrDefault(r => r.Source == ObservationSource.SnapshotDiff);

        // the identity information survives whatever the verdict is: a renamed item must be re-anchored either way
        var anchor = reports.FirstOrDefault(r => r.NewKey is not null);
        var newFileId = reports.Select(r => r.NewFileId).FirstOrDefault(id => id is not null);

        CheckInResult Build(CheckInVerdict verdict, DateTime? at, string? detail, bool adopt = false) => new()
        {
            Verdict = verdict,
            ActivityUtc = at,
            Detail = detail,
            CoveredDays = coverage.CoveredDays,
            Sources = sources,
            CurrentFingerprint = snapshot?.Fingerprint,
            Changes = snapshot?.Changes,
            AdoptAsBaseline = adopt,
            NewKey = anchor?.NewKey,
            NewDisplayPath = anchor?.NewDisplayPath,
            NewFileId = newFileId,
        };

        if (reports.Any(r => r.IsMissing))
            return Build(CheckInVerdict.Missing, null, reports.First(r => r.IsMissing).Detail);

        var strong = reports.Where(r => r.Strength == UseStrength.Strong).ToList();
        if (strong.Count > 0)
        {
            var at = strong.Where(r => r.ActivityUtc.HasValue).Select(r => r.ActivityUtc!.Value).DefaultIfEmpty().Max();
            return Build(CheckInVerdict.StrongUse, at == default ? null : at, Describe(strong));
        }

        var inconclusive = reports.Where(r => !r.Conclusive).ToList();
        if (inconclusive.Count > 0)
            return Build(CheckInVerdict.Inconclusive, null, Describe(inconclusive));
        if (coverage.PoorCoverage)
        {
            return Build(CheckInVerdict.Inconclusive, null,
                $"coverage too poor: only {coverage.CoveredDays} days of use observed in {coverage.CalendarDays} calendar days");
        }

        var weak = reports.Where(r => r.Strength == UseStrength.Weak).ToList();
        if (weak.Count > 0)
        {
            var at = weak.Where(r => r.ActivityUtc.HasValue).Select(r => r.ActivityUtc!.Value).DefaultIfEmpty().Max();
            return Build(CheckInVerdict.WeakUse, at == default ? null : at, Describe(weak), adopt: weak.Any(r => r.AdoptAsBaseline));
        }

        return Build(CheckInVerdict.Quiet, null, null);
    }

    private static string Describe(IEnumerable<ActivityReport> reports) =>
        string.Join("; ", reports.Select(r => r.Detail).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct());
}
