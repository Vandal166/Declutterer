using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;
using Declutterer.Utilities.Helpers;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Abstractions;

public sealed record SmartCleanupResult(
    int SelectedCount,
    long SelectedBytes,
    int AnalyzedCount,
    int SafeCount,
    int LikelyCount,
    int ReviewCount,
    bool BudgetExceeded,
    TimeSpan Elapsed,
    int FoldersLoaded = 0,
    int UnexploredFolders = 0,
    int IncompleteSafetyScans = 0,
    int UninspectedFolders = 0,
    bool StoppedByTimeLimit = false,
    IReadOnlyList<ObservationCandidate>? ObservationCandidates = null,
    ObservationDiscoverySummary? ObservationDiscovery = null)
{
    public static readonly SmartCleanupResult Empty = new(0, 0, 0, 0, 0, 0, false, TimeSpan.Zero);
}

/// <summary>Progress from Smart Select scoring and independent watch-candidate discovery.</summary>
public sealed record SmartCleanupProgress(string Stage, int Done, int Total, string? CurrentPath = null,
    bool IsObservationDiscovery = false);

/// <summary>
/// Evidence based "Smart Select": scores every loaded node for junk likelihood, deletion risk and confidence,
/// annotates the nodes with the result and ticks the checkboxes of the items that qualify for the chosen mode.
/// It never deletes anything - deletion remains an explicit step in the Review &amp; Cleanup window.
/// </summary>
public interface ISmartCleanupService
{
    /// <param name="roots">The scan roots currently shown in the tree.</param>
    /// <param name="mode">How aggressively to tick checkboxes.</param>
    /// <param name="scanOptions">The filters of the current scan; used when folders have to be loaded deeper to look inside them.</param>
    /// <param name="selectedNodes">The selection to replace.</param>
    /// <param name="progress">Scoring and discovery progress. A zero total means the total is not known yet.</param>
    /// <param name="isCurrentScope">Optional guard preventing a run from applying results after its scan scope changes.</param>
    Task<SmartCleanupResult> SelectAsync(IReadOnlyList<TreeNode> roots, SmartSelectMode mode, ScanOptions? scanOptions,
        ObservableHashSet<TreeNode> selectedNodes, CancellationToken cancellationToken = default,
        IProgress<SmartCleanupProgress>? progress = null, Func<bool>? isCurrentScope = null);
}
