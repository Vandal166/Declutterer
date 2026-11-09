using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup.Rules;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Domain.Services.Observation;
using Declutterer.Utilities.Exceptions;

namespace Declutterer.Domain.Services.Cleanup;

public sealed record AnalysisProgress(string Stage, int Done, int Total);

/// <summary>
/// State that can be shared between several analysis passes over the same (growing) tree: completed directory facts are
/// remembered, so a second pass only has to look at what is new.
/// </summary>
public sealed class AnalysisSession
{
    public FactsMemo Memo { get; } = new();

    /// <summary>
    /// What watching items over time showed, loaded once before the run (the rules run in parallel loops and never touch the
    /// database). Null means "no observation evidence": every item is assessed exactly as it was before the feature existed.
    /// </summary>
    public IObservationLookup? Observations { get; set; }

    private readonly ConcurrentDictionary<string, DirectorySafetyScanResult> _safety = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetSafety(string path, out DirectorySafetyScanResult result) =>
        _safety.TryGetValue(PathKey.Normalize(path), out result!);

    /// <summary>
    /// Remembers a safety scan for later passes. A scan that merely ran out of time says nothing about the folder, so it
    /// is NOT remembered: otherwise one slow pass would mark the folder "incomplete" for the rest of the session.
    /// </summary>
    public void PublishSafety(string path, DirectorySafetyScanResult result)
    {
        if (result.BudgetExceeded)
            return;
        _safety[PathKey.Normalize(path)] = result;
    }
}

public sealed class AnalysisResult
{
    public required IReadOnlyDictionary<NodeSnapshot, JunkAssessment> Assessments { get; init; }

    /// <summary>The disk facts collected for the directories that were inspected (the others are simply absent).</summary>
    public IReadOnlyDictionary<NodeSnapshot, DirectoryFacts>? Facts { get; init; }
    public int NodesAnalyzed { get; init; }
    public int DirectoriesInspected { get; init; }
    public int DirectoriesTruncated { get; init; }
    public bool BudgetExceeded { get; init; }

    /// <summary>Directories that were due for a disk inspection but the time budget ran out before it started.</summary>
    public int DirectoriesNotInspected { get; init; }

    /// <summary>Directories whose descendant safety scan could not finish (time, entry or depth limit, unreadable entries).</summary>
    public int IncompleteSafetyScans { get; init; }
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Two-stage cascade:
///   Stage A  metadata only, runs for every loaded node, parallel, no disk access except a few cheap probes.
///   Stage B  bounded disk inspection (<see cref="DirectoryFactsCollector"/>) only for directories that stage A
///            could not rule out; items that are provably alive stop the walk early.
/// Afterwards a safety gate re-validates everything that could become selectable against
/// <see cref="PathSafetyValidator"/>. The engine never touches or deletes anything.
/// </summary>
public sealed class JunkAssessmentEngine
{
    private readonly IReadOnlyList<IAssessmentRule> _rules;
    private readonly DirectoryFactsCollector _collector;
    private readonly DirectorySafetyScanner _safetyScanner;
    private readonly Func<string, string?> _safetyGate;
    private readonly ObservationOptions _observationOptions;

    public JunkAssessmentEngine() : this(DefaultRules(), new DirectoryFactsCollector(), DefaultSafetyGate) { }

    public JunkAssessmentEngine(ObservationOptions observationOptions)
        : this(DefaultRules(observationOptions), new DirectoryFactsCollector(), DefaultSafetyGate, observationOptions) { }

    public JunkAssessmentEngine(IEnumerable<IAssessmentRule> rules, DirectoryFactsCollector collector, Func<string, string?> safetyGate,
        ObservationOptions? observationOptions = null)
    {
        _observationOptions = observationOptions ?? new ObservationOptions();
        _rules = rules.ToList();
        _collector = collector;
        _safetyScanner = new DirectorySafetyScanner();
        _safetyGate = safetyGate;
    }

    /// <summary>Order matters: location rules run first because later rules react to "this is a cache".</summary>
    public static IReadOnlyList<IAssessmentRule> DefaultRules(ObservationOptions? observation = null) => new IAssessmentRule[]
    {
        new KnownLocationRule(),
        new RegenerableArtifactRule(),
        new SensitiveLocationRule(),
        new FilePatternRule(),
        new OwnershipRule(),
        new ProjectRule(),
        new ContentRule(),
        new OrphanedAppDataRule(),
        new StalenessRule(),
        new ObservationRule(observation),
    };

    /// <summary>Returns null if the path may be deleted, otherwise the reason it is protected. Fails closed.</summary>
    public static string? DefaultSafetyGate(string path)
    {
        try
        {
            PathSafetyValidator.Validate(path);
            return null;
        }
        catch (OperationFailedException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            return $"path could not be validated ({ex.Message})";
        }
    }

    public Task<AnalysisResult> AnalyzeAsync(IReadOnlyList<NodeSnapshot> roots, ISystemSnapshot machine, SmartCleanupOptions options,
        IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default, AnalysisSession? session = null) =>
        Task.Run(() => Analyze(roots, machine, options, progress, cancellationToken, session), cancellationToken);

    public AnalysisResult Analyze(IReadOnlyList<NodeSnapshot> roots, ISystemSnapshot machine, SmartCleanupOptions options,
        IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default, AnalysisSession? session = null)
    {
        var started = DateTime.UtcNow;
        bool unlimitedBudget = options.TimeBudget == Timeout.InfiniteTimeSpan;
        var deadline = unlimitedBudget ? DateTime.MaxValue : started + options.TimeBudget; // disk inspection (stage B)
        session ??= new AnalysisSession();

        // Descendant safety scans get their own allotment of TimeBudget. They used to share the clock with the disk
        // inspection, so walking big folders of old personal data used up the time and every junk folder that came
        // afterwards was reported as "incomplete", which can never be selected.
        var safetySpent = TimeSpan.Zero;
        DateTime SafetyDeadline() => unlimitedBudget ? DateTime.MaxValue : DateTime.UtcNow + (options.TimeBudget - safetySpent);
        var passSafety = new ConcurrentDictionary<string, DirectorySafetyScanResult>(StringComparer.OrdinalIgnoreCase);
        int incompleteSafety = 0, notInspected = 0;
        var po = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
        };

        var markers = new MarkerCache();
        var combiner = new AssessmentCombiner(options, _observationOptions);
        var accessReliable = machine.LastAccessTimeReliable ||
                             AccessTimeLooksMaintained(roots.SelectMany(r => r.SelfAndDescendants()).Take(20_000), machine.UtcNow);

        var nodes = new List<NodeSnapshot>();
        var contexts = new List<AssessmentContext>();
        var signals = new List<List<Signal>>();
        var assessments = new List<JunkAssessment>();
        int safetyBudgetExceeded = 0;

        // ── Stage A: metadata only, top-down, level by level ────────────────────
        // Roots are the folders the user chose to scan; they are never candidates themselves. A strongly disposable
        // directory can cover its children only after its full descendant safety scan succeeds.
        progress?.Report(new AnalysisProgress("Analyzing metadata", 0, 0));
        var frontier = roots.SelectMany(r => r.Children).ToList();
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = frontier.ToArray();
            var results = new (AssessmentContext Ctx, List<Signal> Signals, JunkAssessment Assessment)[batch.Length];
            Parallel.For(0, batch.Length, po, i =>
            {
                results[i] = Evaluate(batch[i], null, machine, options, markers, accessReliable, combiner, session.Observations);
            });

            var stageASafety = new DirectorySafetyScanResult?[batch.Length];
            var levelSafetyDeadline = SafetyDeadline();
            var levelClock = System.Diagnostics.Stopwatch.StartNew();
            Parallel.For(0, batch.Length, po, i =>
            {
                if (!batch[i].IsDirectory || results[i].Assessment.HasHardKeep ||
                    !SignalSink.IsDisposableRegion(results[i].Signals))
                    return;

                var scan = GetSafetyScan(batch[i], machine, options, session, passSafety, levelSafetyDeadline, cancellationToken);
                stageASafety[i] = scan;
                results[i].Assessment = ApplySafetyResult(results[i].Assessment, scan);
                if (scan.BudgetExceeded)
                    Interlocked.Exchange(ref safetyBudgetExceeded, 1);
            });
            safetySpent += levelClock.Elapsed;

            var next = new List<NodeSnapshot>();
            for (int i = 0; i < batch.Length; i++)
            {
                nodes.Add(batch[i]);
                contexts.Add(results[i].Ctx);
                signals.Add(results[i].Signals);
                assessments.Add(results[i].Assessment);

                bool coveredByThisNode = !options.PreserveAllDescendants && batch[i].IsDirectory && !results[i].Assessment.HasHardKeep &&
                                         SignalSink.IsDisposableRegion(results[i].Signals) &&
                                         stageASafety[i]?.CanAutoSelect == true;
                if (!coveredByThisNode)
                    next.AddRange(batch[i].Children);
            }
            frontier = next;
        }

        int count = nodes.Count;

        // ── Stage B: bounded disk inspection of directories that are not obviously alive ──
        var facts = new DirectoryFacts?[count];
        int budgetExceeded = safetyBudgetExceeded;
        int inspected = 0, truncated = 0;

        if (options.EnableDeepInspection)
        {
            var targets = Enumerable.Range(0, count)
                .Where(i => nodes[i].IsDirectory && !assessments[i].HasHardKeep && assessments[i].Tier != CleanupTier.Protected &&
                            assessments[i].JunkScore >= options.DeepInspectMinJunk)
                // Promising folders first (stage A already sees junk-like evidence), so the budget is spent on candidates
                // and not on big folders of old personal data that can only end up as "keep". Inside each group deepest
                // first, so parents reuse the children's results.
                .OrderByDescending(i => assessments[i].JunkScore >= 0.50)
                .ThenByDescending(i => nodes[i].Depth)
                .ToArray();

            progress?.Report(new AnalysisProgress("Inspecting folders", 0, targets.Length));
            var memo = session.Memo;
            memo.AddTargets(targets.Select(i => nodes[i].Path));
            var recentCutoff = machine.UtcNow.AddDays(-options.RecentWindowDays);
            int done = 0;

            Parallel.ForEach(targets, po, i =>
            {
                if (memo.TryGet(nodes[i].Path, out var remembered))
                {
                    facts[i] = remembered; // completed in an earlier pass of the same session
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Interlocked.Exchange(ref budgetExceeded, 1);
                    Interlocked.Increment(ref notInspected);
                    return;
                }

                // A folder that already looks like a cache / build output must be fully read (its recent timestamps are
                // meaningless); anything else can stop as soon as it is proven alive.
                bool strongJunk = assessments[i].JunkScore >= 0.80;
                var walk = new FactsWalkOptions
                {
                    MaxEntries = options.MaxEntriesPerDirectory,
                    MaxDepth = options.MaxDepth,
                    DeadlineUtc = deadline,
                    IncludeAccessTime = accessReliable,
                    UseCreationTime = options.UseCreationTime,
                    StopWhenActivityAfterUtc = strongJunk ? null : recentCutoff,
                    Memo = memo,
                };

                var f = _collector.Collect(nodes[i].Path, walk, cancellationToken);
                facts[i] = f;
                if (f.IsComplete)
                    memo.Publish(nodes[i].Path, f);

                Interlocked.Increment(ref inspected);
                if (f.Truncated)
                {
                    Interlocked.Increment(ref truncated);
                    if (DateTime.UtcNow > deadline) Interlocked.Exchange(ref budgetExceeded, 1);
                }
                progress?.Report(new AnalysisProgress("Inspecting folders", Interlocked.Increment(ref done), targets.Length));
            });

            // Re-evaluate everything that now has facts with the complete rule set.
            var withFacts = targets.Where(i => facts[i] is not null).ToArray();
            Parallel.ForEach(withFacts, po, i =>
            {
                var (ctx, sig, a) = Evaluate(nodes[i], facts[i], machine, options, markers, accessReliable, combiner, session.Observations);
                contexts[i] = ctx;
                signals[i] = sig;
                assessments[i] = TryGetKnownSafety(nodes[i].Path, session, passSafety, out var safety)
                    ? ApplySafetyResult(a, safety)
                    : a;
            });
        }

        // ── Safety gate: directory candidates need complete descendant coverage; files need a current path check. ──
        progress?.Report(new AnalysisProgress("Validating", 0, count));
        var gateSafetyDeadline = SafetyDeadline();

        // Big candidates first: if the allotment ever runs out, the folders that would free the most space were scanned.
        Parallel.ForEach(Enumerable.Range(0, count).OrderByDescending(i => nodes[i].Size), po, i =>
        {
            if (assessments[i].Tier < CleanupTier.Review && assessments[i].SelectionBlockReason is null)
                return;

            if (nodes[i].IsDirectory && !assessments[i].HasHardKeep)
            {
                var scan = GetSafetyScan(nodes[i], machine, options, session, passSafety, gateSafetyDeadline, cancellationToken);
                assessments[i] = ApplySafetyResult(assessments[i], scan);
                if (scan.BudgetExceeded)
                    Interlocked.Exchange(ref budgetExceeded, 1);

                if (assessments[i].Tier < CleanupTier.Review && assessments[i].SelectionBlockReason is null)
                    return;
            }

            var blocked = _safetyGate(nodes[i].Path);
            if (blocked is null)
            {
                if (!nodes[i].IsDirectory)
                    assessments[i] = WithSelectionSafety(assessments[i], complete: true, blockReason: null);
                return;
            }

            var withVeto = new List<Signal>(signals[i])
            {
                new(ReasonCode.SystemProtected, EvidenceGroup.Ownership, -8.0, 1.0, blocked, HardKeep: true),
            };
            signals[i] = withVeto;
            assessments[i] = WithSelectionSafety(combiner.Combine(contexts[i], withVeto), complete: false, blockReason: blocked);
        });

        foreach (var scan in passSafety.Values)
        {
            if (scan.Status == DirectorySafetyStatus.Incomplete)
                incompleteSafety++;
        }

        var map = new Dictionary<NodeSnapshot, JunkAssessment>(count);
        var factsMap = new Dictionary<NodeSnapshot, DirectoryFacts>();
        for (int i = 0; i < count; i++)
        {
            map[nodes[i]] = assessments[i];
            if (facts[i] is { } f)
                factsMap[nodes[i]] = f;
        }

        return new AnalysisResult
        {
            Assessments = map,
            Facts = factsMap,
            NodesAnalyzed = count,
            DirectoriesInspected = inspected,
            DirectoriesTruncated = truncated,
            BudgetExceeded = budgetExceeded != 0,
            DirectoriesNotInspected = notInspected,
            IncompleteSafetyScans = incompleteSafety,
            Elapsed = DateTime.UtcNow - started,
        };
    }

    private static bool TryGetKnownSafety(string path, AnalysisSession session,
        ConcurrentDictionary<string, DirectorySafetyScanResult> pass, out DirectorySafetyScanResult result)
    {
        var key = PathKey.Normalize(path);
        if (pass.TryGetValue(key, out result!))
            return true;

        if (!session.TryGetSafety(path, out result!))
            return false;

        // Include reused deterministic results in this pass's summary as well as newly run scans.
        pass.TryAdd(key, result);
        return true;
    }

    private DirectorySafetyScanResult GetSafetyScan(NodeSnapshot node, ISystemSnapshot machine, SmartCleanupOptions options,
        AnalysisSession session, ConcurrentDictionary<string, DirectorySafetyScanResult> pass, DateTime deadline,
        CancellationToken cancellationToken)
    {
        if (TryGetKnownSafety(node.Path, session, pass, out var cached))
            return cached;

        var result = _safetyScanner.Scan(node.Path, machine, options, deadline, _safetyGate, cancellationToken);
        session.PublishSafety(node.Path, result); // time-outs are not remembered across passes ...
        pass[PathKey.Normalize(node.Path)] = result; // ... but are within this pass, so nothing is scanned twice
        return result;
    }

    /// <summary>Completes the descendant safety gate for a stale Keep folder offered for observation.</summary>
    public DirectorySafetyScanResult EnsureObservationSafety(NodeSnapshot node, ISystemSnapshot machine, SmartCleanupOptions options,
        AnalysisSession session, CancellationToken cancellationToken = default)
    {
        if (session.TryGetSafety(node.Path, out var cached))
            return cached;

        var result = _safetyScanner.Scan(node.Path, machine, options, DateTime.MaxValue, _safetyGate, cancellationToken);
        session.PublishSafety(node.Path, result);
        return result;
    }

    private static JunkAssessment ApplySafetyResult(JunkAssessment assessment, DirectorySafetyScanResult scan)
    {
        var tier = assessment.Tier;
        if (scan.Status == DirectorySafetyStatus.Blocked && tier is not CleanupTier.Protected)
            tier = CleanupTier.Keep;
        else if (scan.Status == DirectorySafetyStatus.Incomplete && tier == CleanupTier.Safe)
            tier = CleanupTier.Review;

        return WithSelectionSafety(WithTier(assessment, tier), scan.CanAutoSelect, scan.BlockReason);
    }

    private static JunkAssessment WithTier(JunkAssessment assessment, CleanupTier tier) => new()
    {
        JunkScore = assessment.JunkScore,
        DeletionRisk = assessment.DeletionRisk,
        Confidence = assessment.Confidence,
        Coverage = assessment.Coverage,
        Tier = tier,
        Reasons = assessment.Reasons,
        SelectionSafetyComplete = assessment.SelectionSafetyComplete,
        SelectionBlockReason = assessment.SelectionBlockReason,
    };

    private static JunkAssessment WithSelectionSafety(JunkAssessment assessment, bool complete, string? blockReason) => new()
    {
        JunkScore = assessment.JunkScore,
        DeletionRisk = assessment.DeletionRisk,
        Confidence = assessment.Confidence,
        Coverage = assessment.Coverage,
        Tier = assessment.Tier,
        Reasons = assessment.Reasons,
        SelectionSafetyComplete = complete,
        SelectionBlockReason = blockReason,
    };

    private (AssessmentContext, List<Signal>, JunkAssessment) Evaluate(NodeSnapshot node, DirectoryFacts? facts, ISystemSnapshot machine,
        SmartCleanupOptions options, MarkerCache markers, bool accessReliable, AssessmentCombiner combiner, IObservationLookup? observations = null)
    {
        ObservationSummary? observed = null;
        observations?.TryGet(node.Path, out observed);
        var ctx = new AssessmentContext(node, machine, options, markers, facts, accessReliable, observed);
        var sink = new SignalSink();
        foreach (var rule in _rules)
            rule.Evaluate(ctx, sink);

        // A pin / snooze protects the folder it was set on (ObservationRule) - and must not be undone by selecting a folder ABOVE it,
        // which includes the case where the pinned folder is not loaded in the tree at all.
        if (node.IsDirectory && observations?.ContainsKeptPath(node.Path) == true)
            sink.Add(ReasonCode.ContainsKeptFolder, EvidenceGroup.Observation, -5.0, 0.9,
                "a folder inside it is pinned or snoozed in the Watchlist: selecting this one would delete it too", hardKeep: true);

        var list = sink.Signals.ToList();
        return (ctx, list, combiner.Combine(ctx, list));
    }

    /// <summary>
    /// NTFS frequently has last-access updates switched off (and Windows 10+ manages it itself). When they are off,
    /// access time == creation time forever. If a sample of files shows access times clearly after creation, the
    /// timestamps are alive and can be used as usage evidence; otherwise they are ignored.
    /// </summary>
    public static bool AccessTimeLooksMaintained(IEnumerable<NodeSnapshot> nodes, DateTime nowUtc)
    {
        return AccessTimeProbe.LooksMaintained(Samples(nodes, nowUtc), nowUtc);

        static IEnumerable<(DateTime AccessUtc, DateTime WriteUtc, DateTime CreatedUtc)> Samples(IEnumerable<NodeSnapshot> source, DateTime now)
        {
            foreach (var n in source)
            {
                if (n.IsDirectory || n.LastAccessUtc is not { } access || n.LastWriteUtc is not { } write)
                    continue;
                if ((now - write).TotalDays < 7)
                    continue; // fresh files always look "accessed after creation"; skip before paying for the creation-time lookup

                DateTime created;
                try { created = File.GetCreationTimeUtc(n.Path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { continue; }
                yield return (access, write, created);
            }
        }
    }
}
