using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Domain.Services.Scanning;
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
    private readonly IObservationLedger? _ledger;
    private readonly ObservationOptions _observationOptions;
    private readonly TimeProvider _time;
    private readonly ObservationCandidateDiscoveryService _observationDiscovery;

    public SmartCleanupService(JunkAssessmentEngine engine, SmartCleanupSelector selector, ISystemSnapshotProvider snapshotProvider,
        ITreeNavigationService treeNavigation, ICommandService commandService, IDispatcher dispatcher, SmartCleanupOptions? options = null,
        IObservationLedger? observationLedger = null, ObservationOptions? observationOptions = null, TimeProvider? timeProvider = null,
        ScanFilterService? scanFilterService = null)
    {
        _ledger = observationLedger;
        _observationOptions = observationOptions ?? new ObservationOptions();
        _time = timeProvider ?? TimeProvider.System;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _treeNavigation = treeNavigation ?? throw new ArgumentNullException(nameof(treeNavigation));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? new SmartCleanupOptions();
        _observationDiscovery = new ObservationCandidateDiscoveryService(
            scanFilterService ?? new ScanFilterService(new ScanFilterBuilder()), _engine);
    }

    public async Task<SmartCleanupResult> SelectAsync(IReadOnlyList<TreeNode> roots, SmartSelectMode mode, ScanOptions? scanOptions,
        ObservableHashSet<TreeNode> selectedNodes, CancellationToken cancellationToken = default,
        IProgress<SmartCleanupProgress>? progress = null, Func<bool>? isCurrentScope = null)
    {
        if (roots.Count == 0)
            return SmartCleanupResult.Empty;

        var clock = Stopwatch.StartNew();
        var startedUtc = _time.GetUtcNow().UtcDateTime;
        var machine = await _snapshotProvider.CaptureAsync(cancellationToken);

        // Observation evidence is only used when the user switched the feature on and the ledger is usable. A ledger that fails to load
        // simply means "no observation evidence": the run is then exactly what it was before the feature existed.
        bool observing = _observationOptions.Enabled && _ledger is { IsAvailable: true };
        IObservationLookup? lookup = null;
        if (observing)
        {
            try
            {
                lookup = await _ledger!.LoadLookupAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Smart Select could not load the observation ledger; continuing without observation evidence");
            }
        }
        var session = new AnalysisSession { Observations = lookup };
        var attempted = new HashSet<TreeNode>();

        List<NodeSnapshot> snapshots;
        AnalysisResult analysis;
        int round = 0, foldersLoaded = 0;
        bool stoppedByTime = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The tree is a UI object (lazy loading mutates it on the UI thread), so it is copied there first.
            snapshots = await _dispatcher.InvokeAsync(() => roots.Select(r => NodeSnapshot.FromTreeNode(r)).ToList());
            var engineProgress = progress is null ? null : new Progress<AnalysisProgress>(p =>
                progress.Report(new SmartCleanupProgress(p.Stage, p.Done, p.Total)));
            analysis = await _engine.AnalyzeAsync(snapshots, machine, _options, engineProgress, cancellationToken, session);

            // A pass that ran out of time used to end the whole run, which meant a big scan was never looked into at all.
            // Completed facts and safety scans are remembered by the session, so another pass only has to do what is new.
            if (round++ >= _options.DeepenMaxRounds)
                break;
            if (clock.Elapsed >= _options.TotalTimeBudget)
            {
                stoppedByTime = true;
                break;
            }

            var targets = DeepeningPlanner.SelectTargets(snapshots, analysis.Assessments, _options, attempted);
            if (targets.Count == 0)
                break;
            foreach (var t in targets)
                attempted.Add(t.Source!);

            Log.Information("Smart Select round {Round}: loading {Count} folders to look inside", round, targets.Count);
            await LoadDeeperAsync(targets, scanOptions, cancellationToken);
            foldersLoaded += targets.Count;
            if (clock.Elapsed >= _options.TotalTimeBudget)
            {
                stoppedByTime = true;
                break;
            }
        }

        // folders that would have been worth opening but were not (round / depth / count limits)
        int unexplored = DeepeningPlanner.Candidates(snapshots, analysis.Assessments, _options)
            .Count(t => !attempted.Contains(t.Source!));

        var plan = _selector.Plan(snapshots, analysis.Assessments, mode, _options);

        // Suspicious or independently stale folders are discovered separately from the selection plan below.
        ObservationDiscoveryResult? observationDiscovery = null;
        if (observing)
        {
            progress?.Report(new SmartCleanupProgress("Starting independent watch discovery", 0, 0, IsObservationDiscovery: true));
            observationDiscovery = await _observationDiscovery.DiscoverAsync(roots, scanOptions, machine, _options,
                _observationOptions, lookup, plan.Selected.Select(n => n.Path), progress, cancellationToken);
        }

        bool scopeCurrent = true;
        await _dispatcher.InvokeAsync(() =>
        {
            if (isCurrentScope is not null && !isCurrentScope())
            {
                scopeCurrent = false;
                return;
            }

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
        if (observing)
        {
            // this run read the tree itself: later access-time comparisons must not mistake that for use
            await _ledger!.RecordOwnActivityAsync(startedUtc, _time.GetUtcNow().UtcDateTime, "smart-select", CancellationToken.None);
        }
        int selectedCount = scopeCurrent ? plan.Selected.Count : 0;
        long selectedBytes = scopeCurrent ? plan.TotalBytes : 0;
        Log.Information(
            "Smart Select ({Mode}): analysed {Analyzed} nodes, inspected {Inspected} folders, selected {Selected} items in {Elapsed} ms " +
            "(loaded {Loaded} folders, {Unexplored} left unexplored, {IncompleteScans} incomplete safety scans, {NotInspected} not inspected)",
            mode, analysis.NodesAnalyzed, analysis.DirectoriesInspected, selectedCount, clock.ElapsedMilliseconds,
            foldersLoaded, unexplored, analysis.IncompleteSafetyScans, analysis.DirectoriesNotInspected);

        return new SmartCleanupResult(selectedCount, selectedBytes,
            analysis.NodesAnalyzed, scopeCurrent ? plan.SafeCount : 0, scopeCurrent ? plan.LikelyCount : 0,
            scopeCurrent ? plan.ReviewCount : 0, analysis.BudgetExceeded, clock.Elapsed, foldersLoaded, unexplored, analysis.IncompleteSafetyScans,
            analysis.DirectoriesNotInspected, stoppedByTime, observationDiscovery?.Candidates, observationDiscovery?.Summary);
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
