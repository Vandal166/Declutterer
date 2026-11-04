using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Domain.Services.Observation;

public enum ObservationCandidateReason
{
    Suspicious,
    Stale,
}

/// <summary>An unselected folder offered for observation, independent of any UI tree node.</summary>
public sealed record ObservationCandidate(
    string Path,
    string Name,
    long Size,
    ObservationClass Class,
    CleanupTier Tier,
    double JunkScore,
    double DeletionRisk,
    DateTime? LastKnownActivityUtc,
    ObservationCandidateReason Reason,
    IReadOnlyList<ReasonCode> ReasonCodes);

/// <summary>Coverage collected while walking the scan roots, before assessment and nomination.</summary>
public sealed record ObservationDiscoveryCoverage(
    int FoldersExamined = 0,
    int HiddenFoldersSkipped = 0,
    int FilteredFoldersSkipped = 0,
    int ReparseFoldersSkipped = 0,
    int SystemFoldersSkipped = 0,
    int InaccessibleDirectories = 0,
    int DepthLimitedDirectories = 0,
    int EntryLimitedDirectories = 0,
    bool IsComplete = true,
    bool WasCancelled = false,
    string? PartialReason = null);

/// <summary>A transparent account of nomination, exclusions, capacity and scan coverage.</summary>
public sealed record ObservationDiscoverySummary(
    int FoldersExamined,
    int CandidatesFound,
    int CandidatesReturned,
    int AvailableWatchSlots,
    int ExcludedByCapacity,
    int ExcludedSelectedOverlap,
    int ExcludedAlreadyWatched,
    int ExcludedNested,
    int ExcludedUnsafeOrIncomplete,
    int ExcludedValuableOrSensitive,
    int ExcludedNotEligible,
    int HiddenFoldersSkipped,
    int FilteredFoldersSkipped,
    int ReparseFoldersSkipped,
    int SystemFoldersSkipped,
    int InaccessibleDirectories,
    int DepthLimitedDirectories,
    int EntryLimitedDirectories,
    bool IsComplete,
    bool WasCancelled,
    string? PartialReason);

public sealed record ObservationDiscoveryResult(
    IReadOnlyList<ObservationCandidate> Candidates,
    ObservationDiscoverySummary Summary);

/// <summary>Decides whether a fully assessed folder may be observed at all.</summary>
public static class ObservationClassifier
{
    public static ObservationClass? Classify(NodeSnapshot node, JunkAssessment assessment, DirectoryFacts? facts,
        ISystemSnapshot machine, ObservationOptions options) =>
        Classify(node, assessment, facts, machine, options, staleKeepSafetyComplete: false, out _);

    public static ObservationClass? Classify(NodeSnapshot node, JunkAssessment assessment, DirectoryFacts? facts,
        ISystemSnapshot machine, ObservationOptions options, bool staleKeepSafetyComplete, out ObservationCandidateReason reason)
    {
        reason = ObservationCandidateReason.Suspicious;
        if (!node.IsDirectory || node.IsRoot)
            return null;

        bool suspicious = assessment.Tier is CleanupTier.Review or CleanupTier.Likely;
        bool staleKeep = assessment.Tier == CleanupTier.Keep && HasStaleEvidence(assessment);
        if (!suspicious && !staleKeep)
            return null;
        if (staleKeep)
            reason = ObservationCandidateReason.Stale;

        if (assessment.HasHardKeep || assessment.SelectionBlockReason is not null ||
            !(assessment.SelectionSafetyComplete || staleKeep && staleKeepSafetyComplete))
            return null;

        // Both content facts and the descendant safety scan must be complete. An early stop is useful evidence of activity
        // for scoring, but it is not a complete inventory for deciding what silence would mean.
        if (facts is null || !facts.IsComplete || facts.HasCredentialFile)
            return null;

        bool inAppData = machine.IsUnderAppDataRoot(node.Path, out _);
        bool disposableRegion = SignalSink.IsDisposableRegion(assessment.Reasons);
        if (!disposableRegion && facts.ValuableShare(inAppData) >= options.ValuableShareLimit)
            return null;
        if (assessment.Reasons.Any(r => r.Code is ReasonCode.ContainsUserDocuments or ReasonCode.ContainsCredentials or ReasonCode.BrowserProfile
                or ReasonCode.UserSettingsOrSaves or ReasonCode.SensitiveLocation or ReasonCode.SystemProtected or ReasonCode.SyncedFolder) ||
            machine.IsInSyncedFolder(node.Path, out _))
            return null;

        if (inAppData)
            return ObservationClass.AppData;
        if (assessment.Reasons.Any(r => r.Code is ReasonCode.ProjectRoot or ReasonCode.VersionControlRepo) ||
            (facts.OwnMarkers & ProjectMarkerProbe.ProjectLike) != 0)
            return ObservationClass.ProjectFolder;
        return ObservationClass.GenericFolder;
    }

    public static bool HasStaleEvidence(JunkAssessment assessment) =>
        assessment.Reasons.Any(r => r.Code is ReasonCode.LongUntouched or ReasonCode.OrphanedAppData);
}

public static class ObservationNominator
{
    /// <summary>
    /// Returns stable, outermost candidates from an independently discovered tree. Selected paths are excluded in either
    /// ancestor direction so no folder can be offered for watching while it is selected for cleanup review.
    /// </summary>
    public static ObservationDiscoveryResult Nominate(IEnumerable<NodeSnapshot> roots, AnalysisResult analysis, ISystemSnapshot machine,
        ObservationOptions options, IObservationLookup? existing, IEnumerable<string>? selectedPaths = null,
        ISet<string>? staleKeepSafetyCompletePaths = null, ObservationDiscoveryCoverage? coverage = null)
    {
        coverage ??= new ObservationDiscoveryCoverage();
        var selected = (selectedPaths ?? Array.Empty<string>())
            .Select(PathKey.Normalize).Where(p => p.Length > 0).ToArray();
        var safeKeep = staleKeepSafetyCompletePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<ObservationCandidate>();
        int foldersExamined = coverage.FoldersExamined;
        int selectedOverlap = 0, watchedOverlap = 0, unsafeOrIncomplete = 0, valuableOrSensitive = 0, notEligible = 0;

        foreach (var node in roots.SelectMany(r => r.SelfAndDescendants()))
        {
            if (!node.IsDirectory || node.IsRoot)
                continue;
            if (coverage.FoldersExamined == 0)
                foldersExamined++;
            if (!analysis.Assessments.TryGetValue(node, out var assessment))
            {
                notEligible++;
                continue;
            }

            DirectoryFacts? facts = null;
            analysis.Facts?.TryGetValue(node, out facts);
            bool safetyComplete = safeKeep.Contains(PathKey.Normalize(node.Path));
            var cls = ObservationClassifier.Classify(node, assessment, facts, machine, options, safetyComplete, out var reason);
            if (cls is null)
            {
                if (assessment.Tier is CleanupTier.Review or CleanupTier.Likely ||
                    assessment.Tier == CleanupTier.Keep && ObservationClassifier.HasStaleEvidence(assessment))
                {
                    if (assessment.HasHardKeep || assessment.SelectionBlockReason is not null ||
                        !assessment.SelectionSafetyComplete && !safetyComplete || facts is null || !facts.IsComplete)
                        unsafeOrIncomplete++;
                    else if (facts.HasCredentialFile || assessment.Reasons.Any(r => r.Code is ReasonCode.ContainsUserDocuments or
                                 ReasonCode.ContainsCredentials or ReasonCode.BrowserProfile or ReasonCode.UserSettingsOrSaves or
                                 ReasonCode.SensitiveLocation or ReasonCode.SystemProtected) ||
                             !SignalSink.IsDisposableRegion(assessment.Reasons) &&
                             facts.ValuableShare(machine.IsUnderAppDataRoot(node.Path, out _)) >= options.ValuableShareLimit)
                        valuableOrSensitive++;
                    else
                        notEligible++;
                }
                else
                    notEligible++;
                continue;
            }

            if (selected.Any(path => PathsOverlap(path, node.Path)))
            {
                selectedOverlap++;
                continue;
            }
            if (existing?.OverlapsExisting(node.Path) == true)
            {
                watchedOverlap++;
                continue;
            }

            DateTime? activity = facts!.NewestActivityUtc > DateTime.MinValue ? facts.NewestActivityUtc : node.LastWriteUtc;
            if (machine.TryGetRecentOpen(node.Path, out var opened) && (activity is null || opened > activity))
                activity = opened;

            var reasons = assessment.Reasons.Select(r => r.Code).Distinct().ToArray();
            found.Add(new ObservationCandidate(node.Path, node.Name, node.Size, cls.Value, assessment.Tier,
                assessment.JunkScore, assessment.DeletionRisk, activity, reason, reasons));
        }

        var topLevel = TopLevel(found);
        int nested = Math.Max(0, found.Count - topLevel.Count);
        var ordered = topLevel
            // The old 16 MiB threshold remains a ranking hint only; smaller folders stay eligible.
            .OrderByDescending(c => c.Size >= options.ObserveMinBytes)
            .ThenByDescending(c => c.Size)
            .ThenBy(c => PathKey.Normalize(c.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => PathKey.Normalize(c.Path), StringComparer.Ordinal)
            .ToList();

        int slots = Math.Max(0, options.MaxWatchItems - (existing?.ActiveCount ?? 0));
        var candidates = ordered.Take(slots).ToList();
        int byCapacity = ordered.Count - candidates.Count;
        bool complete = coverage.IsComplete && unsafeOrIncomplete == 0;
        string? partialReason = coverage.PartialReason;
        if (unsafeOrIncomplete > 0)
            partialReason = string.IsNullOrWhiteSpace(partialReason)
                ? $"{unsafeOrIncomplete} folder(s) were blocked by safety or had incomplete facts / checks"
                : $"{partialReason}; {unsafeOrIncomplete} folder(s) were blocked by safety or had incomplete facts / checks";

        return new ObservationDiscoveryResult(candidates, new ObservationDiscoverySummary(
            foldersExamined, found.Count, candidates.Count, slots, byCapacity, selectedOverlap, watchedOverlap, nested,
            unsafeOrIncomplete, valuableOrSensitive, notEligible, coverage.HiddenFoldersSkipped, coverage.FilteredFoldersSkipped,
            coverage.ReparseFoldersSkipped, coverage.SystemFoldersSkipped, coverage.InaccessibleDirectories,
            coverage.DepthLimitedDirectories, coverage.EntryLimitedDirectories, complete, coverage.WasCancelled, partialReason));
    }

    public static bool PathsOverlap(string first, string second) =>
        PathKey.IsUnderOrEqual(first, second) || PathKey.IsUnderOrEqual(second, first);

    /// <summary>Keeps the outermost candidates; duplicate paths are removed as part of the same pass.</summary>
    public static IReadOnlyList<ObservationCandidate> TopLevel(IEnumerable<ObservationCandidate> candidates)
    {
        var kept = new List<ObservationCandidate>();
        var keptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates
                     .OrderBy(c => PathKey.Normalize(c.Path).Length)
                     .ThenBy(c => PathKey.Normalize(c.Path), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => PathKey.Normalize(c.Path), StringComparer.Ordinal))
        {
            bool nested = ObservationKey.SelfAndAncestors(c.Path).Any(keptKeys.Contains);
            if (nested)
                continue;
            kept.Add(c);
            keptKeys.Add(ObservationKey.From(c.Path));
        }
        return kept;
    }
}
