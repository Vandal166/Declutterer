using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Models;
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
    bool StoppedByTimeLimit = false)
{
    public static readonly SmartCleanupResult Empty = new(0, 0, 0, 0, 0, 0, false, TimeSpan.Zero);
}

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
    Task<SmartCleanupResult> SelectAsync(IReadOnlyList<TreeNode> roots, SmartSelectMode mode, ScanOptions? scanOptions,
        ObservableHashSet<TreeNode> selectedNodes, CancellationToken cancellationToken = default);
}
