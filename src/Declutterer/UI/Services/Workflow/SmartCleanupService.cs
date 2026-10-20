using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Utilities.Helpers;
using Serilog;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Services.Workflow;

/// <summary>
/// Orchestrates one Smart Select run:
///   1. capture machine knowledge (installed apps, running programs, recent files ...)
///   2. snapshot the loaded tree on the UI thread, analyse it on a worker thread
///   3. if big, non-junk folders have not been loaded yet, load them (bounded) and analyse again - disk facts of
///      the first pass are reused through the <see cref="AnalysisSession"/>
///   4. annotate nodes with their assessment and tick the checkboxes of the items that qualify for the mode.
/// Nothing is deleted here; the user still has to go through Review &amp; Cleanup.
/// </summary>
public sealed class SmartCleanupService : ISmartCleanupService
{
    private readonly JunkAssessmentEngine _engine;
    private readonly SmartCleanupSelector _selector;
    private readonly ISystemSnapshotProvider _snapshotProvider;
    private readonly ITreeNavigationService _treeNavigation;
    private readonly ICommandService _commandService;
    private readonly IDispatcher _dispatcher;
    private readonly SmartCleanupOptions _options;

    public SmartCleanupService(JunkAssessmentEngine engine, SmartCleanupSelector selector, ISystemSnapshotProvider snapshotProvider,
        ITreeNavigationService treeNavigation, ICommandService commandService, IDispatcher dispatcher, SmartCleanupOptions? options = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _treeNavigation = treeNavigation ?? throw new ArgumentNullException(nameof(treeNavigation));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? new SmartCleanupOptions();
    }

    public async Task<SmartCleanupResult> SelectAsync(IReadOnlyList<TreeNode> roots, SmartSelectMode mode, ScanOptions? scanOptions,
        ObservableHashSet<TreeNode> selectedNodes, CancellationToken cancellationToken = default)
    {
        if (roots.Count == 0)
            return SmartCleanupResult.Empty;

        var clock = Stopwatch.StartNew();
        var machine = await _snapshotProvider.CaptureAsync(cancellationToken);
        var session = new AnalysisSession();
        var attempted = new HashSet<TreeNode>();

        List<NodeSnapshot> snapshots;
        AnalysisResult analysis;
        int round = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The tree is a UI object (lazy loading mutates it on the UI thread), so it is copied there first.
            snapshots = await _dispatcher.InvokeAsync(() => roots.Select(r => NodeSnapshot.FromTreeNode(r)).ToList());
            analysis = await _engine.AnalyzeAsync(snapshots, machine, _options, null, cancellationToken, session);

            if (round++ >= _options.DeepenMaxRounds || analysis.BudgetExceeded)
                break;

            var targets = DeepeningPlanner.SelectTargets(snapshots, analysis.Assessments, _options)
                .Where(t => attempted.Add(t.Source!))
                .ToList();
            if (targets.Count == 0)
                break;

            Log.Information("Smart Select round {Round}: loading {Count} folders to look inside", round, targets.Count);
            await LoadDeeperAsync(targets, scanOptions, cancellationToken);
        }

        var plan = _selector.Plan(snapshots, analysis.Assessments, mode, _options);

        await _dispatcher.InvokeAsync(() =>
        {
            // annotate every node of the final snapshot (nodes below a region that is taken whole carry no assessment)
            foreach (var snap in snapshots.SelectMany(s => s.SelfAndDescendants()))
            {
                if (snap.Source is { } node)
                    node.Assessment = analysis.Assessments.TryGetValue(snap, out var a) ? a : null;
            }

            _commandService.DeselectAll(selectedNodes);
            foreach (var snap in plan.Selected)
            {
                if (snap.Source is not { } node)
                    continue;
                node.IsCheckboxSelected = true;
                selectedNodes.Add(node);
            }
        });

        clock.Stop();
        Log.Information(
            "Smart Select ({Mode}): analysed {Analyzed} nodes, inspected {Inspected} folders, selected {Selected} items in {Elapsed} ms",
            mode, analysis.NodesAnalyzed, analysis.DirectoriesInspected, plan.Selected.Count, clock.ElapsedMilliseconds);

        return new SmartCleanupResult(plan.Selected.Count, plan.TotalBytes, analysis.NodesAnalyzed, plan.SafeCount, plan.LikelyCount,
            plan.ReviewCount, analysis.BudgetExceeded, clock.Elapsed);
    }

    private async Task LoadDeeperAsync(IReadOnlyList<NodeSnapshot> targets, ScanOptions? scanOptions, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(targets,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (target, token) =>
            {
                try
                {
                    // the directory listing runs on a worker; the tree navigation service marshals the Children updates itself
                    await Task.Run(() => _treeNavigation.LoadChildrenForNodeAsync(target.Source!, scanOptions), token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warning(ex, "Smart Select could not look inside {Path}", target.Path);
                }
            });
    }
}
