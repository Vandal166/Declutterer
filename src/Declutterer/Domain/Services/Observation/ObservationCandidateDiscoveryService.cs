using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Scanning;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// Walks the selected scan roots independently of the lazy UI tree, then assesses the discovered directories with the same
/// machine snapshot and analysis engine as Smart Select. Directory links and the same hidden/system entries that the normal
/// scan excludes are never followed.
/// </summary>
public sealed class ObservationCandidateDiscoveryService
{
    private const FileAttributes AlwaysSkipped = FileAttributes.System | FileAttributes.Temporary | FileAttributes.Offline |
                                                 FileAttributes.Encrypted | FileAttributes.ReparsePoint | FileAttributes.Device;

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        ReturnSpecialDirectories = false,
    };

    private readonly ScanFilterService _scanFilters;
    private readonly JunkAssessmentEngine _engine;

    public ObservationCandidateDiscoveryService(ScanFilterService scanFilters, JunkAssessmentEngine engine)
    {
        _scanFilters = scanFilters ?? throw new ArgumentNullException(nameof(scanFilters));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public async Task<ObservationDiscoveryResult> DiscoverAsync(IReadOnlyList<TreeNode> roots, ScanOptions? scanOptions,
        ISystemSnapshot machine, SmartCleanupOptions analysisOptions, ObservationOptions observationOptions,
        IObservationLookup? existing, IEnumerable<string> selectedPaths, IProgress<SmartCleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var coverage = new MutableCoverage();
        var filter = _scanFilters.CreateFilter(scanOptions);
        var includeHidden = scanOptions?.IncludeHidden ?? false;

        List<NodeSnapshot> discovered;
        try
        {
            discovered = await Task.Run(() => WalkRoots(roots, filter, includeHidden, analysisOptions, coverage, progress, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            coverage.WasCancelled = true;
            coverage.IsComplete = false;
            coverage.PartialReason ??= "discovery was cancelled";
            return EmptyResult(discovered: Array.Empty<NodeSnapshot>(), coverage.ToCoverage(), existing, observationOptions, machine);
        }

        var scanCoverage = coverage.ToCoverage();
        var session = new AnalysisSession { Observations = existing };
        var discoveryOptions = CreateAnalysisOptions(analysisOptions);
        AnalysisResult analysis;
        try
        {
            progress?.Report(new SmartCleanupProgress("Assessing discovered folders", 0, coverage.FoldersExamined));
            var engineProgress = progress is null ? null : new Progress<AnalysisProgress>(p =>
                progress.Report(new SmartCleanupProgress(p.Stage, p.Done, p.Total, IsObservationDiscovery: true)));
            analysis = await _engine.AnalyzeAsync(discovered, machine, discoveryOptions, engineProgress, cancellationToken, session);
        }
        catch (OperationCanceledException)
        {
            scanCoverage = scanCoverage with { IsComplete = false, WasCancelled = true, PartialReason = "assessment was cancelled" };
            return EmptyResult(discovered, scanCoverage, existing, observationOptions, machine);
        }

        if (analysis.Facts is not null)
        {
            foreach (var pair in analysis.Facts)
                if (pair.Value.IsComplete)
                    pair.Key.Size = pair.Value.TotalBytes;
        }

        var staleKeepSafety = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int staleKeepSafetyFailures = 0;
        var staleKeepNodes = analysis.Assessments
            .Where(pair => pair.Value.Tier == CleanupTier.Keep && ObservationClassifier.HasStaleEvidence(pair.Value) &&
                           !pair.Value.HasHardKeep && pair.Value.SelectionBlockReason is null &&
                           analysis.Facts?.TryGetValue(pair.Key, out var facts) == true && facts.IsComplete)
            .Select(pair => pair.Key)
            .ToArray();

        if (staleKeepNodes.Length > 0)
            progress?.Report(new SmartCleanupProgress("Checking stale folders for safety", 0, staleKeepNodes.Length, IsObservationDiscovery: true));

        try
        {
            for (int i = 0; i < staleKeepNodes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = staleKeepNodes[i];
                var safety = _engine.EnsureObservationSafety(node, machine, discoveryOptions, session, cancellationToken);
                if (safety.Status == DirectorySafetyStatus.Safe)
                    staleKeepSafety.Add(PathKey.Normalize(node.Path));
                else
                    staleKeepSafetyFailures++;
                progress?.Report(new SmartCleanupProgress("Checking stale folders for safety", i + 1, staleKeepNodes.Length,
                    node.Path, IsObservationDiscovery: true));
            }
        }
        catch (OperationCanceledException)
        {
            scanCoverage = scanCoverage with { IsComplete = false, WasCancelled = true,
                PartialReason = AddReason(scanCoverage.PartialReason, "stale-folder safety checks were cancelled") };
        }

        if (staleKeepSafetyFailures > 0)
        {
            scanCoverage = scanCoverage with
            {
                IsComplete = false,
                PartialReason = AddReason(scanCoverage.PartialReason,
                    $"{staleKeepSafetyFailures} stale folder(s) did not pass complete safety checks"),
            };
        }

        progress?.Report(new SmartCleanupProgress("Finalizing watch suggestions", coverage.FoldersExamined,
            coverage.FoldersExamined, IsObservationDiscovery: true));
        return ObservationNominator.Nominate(discovered, analysis, machine, observationOptions, existing,
            selectedPaths, staleKeepSafety, scanCoverage);
    }

    private static List<NodeSnapshot> WalkRoots(IReadOnlyList<TreeNode> roots, Func<FileSystemInfoWrapper, bool>? filter,
        bool includeHidden, SmartCleanupOptions options, MutableCoverage coverage, IProgress<SmartCleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rootPaths = ObservationNominator.TopLevel(roots.Select(r => new ObservationCandidate(
                PathKey.Normalize(r.FullPath), string.Empty, 0, ObservationClass.GenericFolder, CleanupTier.Keep, 0, 0, null,
                ObservationCandidateReason.Stale, Array.Empty<ReasonCode>())))
            .Select(c => c.Path).ToArray();
        var snapshots = new List<NodeSnapshot>();
        var pending = new Stack<NodeSnapshot>();

        foreach (var rootPath in rootPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = Path.GetFullPath(rootPath);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    coverage.ReparseFoldersSkipped++;
                    coverage.MarkPartial("a selected scan root is a reparse point");
                    continue;
                }
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    coverage.InaccessibleDirectories++;
                    coverage.MarkPartial("a selected scan root is not a directory");
                    continue;
                }

                var info = new DirectoryInfo(path);
                var root = new NodeSnapshot
                {
                    Path = path,
                    Name = info.Name,
                    IsDirectory = true,
                    LastWriteUtc = SafeTime(() => info.LastWriteTimeUtc),
                    LastAccessUtc = SafeTime(() => info.LastAccessTimeUtc),
                    Depth = 0,
                };
                snapshots.Add(root);
                pending.Push(root);
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                coverage.InaccessibleDirectories++;
                coverage.MarkPartial("a selected scan root could not be read");
            }
        }

        var lastProgress = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = pending.Pop();
            if (parent.Depth >= options.MaxDepth)
            {
                coverage.DepthLimitedDirectories++;
                coverage.MarkPartial("the configured directory depth limit was reached");
                continue;
            }

            coverage.FoldersExamined++;
            if (coverage.FoldersExamined == 1 || coverage.FoldersExamined - lastProgress >= 25)
            {
                lastProgress = coverage.FoldersExamined;
                progress?.Report(new SmartCleanupProgress("Discovering folders", coverage.FoldersExamined, 0,
                    parent.Path, IsObservationDiscovery: true));
            }

            int entries = 0;
            try
            {
                foreach (var entry in new DirectoryInfo(parent.Path).EnumerateFileSystemInfos("*", Enumeration))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > options.MaxEntriesPerDirectory)
                    {
                        coverage.EntryLimitedDirectories++;
                        coverage.MarkPartial("the per-directory entry limit was reached");
                        break;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = entry.Attributes;
                    }
                    catch (Exception ex) when (IsAccessException(ex))
                    {
                        coverage.InaccessibleDirectories++;
                        coverage.MarkPartial("some directory entries could not be inspected");
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) == 0)
                        continue;
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        coverage.ReparseFoldersSkipped++;
                        continue;
                    }
                    if ((attributes & AlwaysSkipped) != 0)
                    {
                        coverage.SystemFoldersSkipped++;
                        continue;
                    }
                    if (!includeHidden && (attributes & FileAttributes.Hidden) != 0)
                    {
                        coverage.HiddenFoldersSkipped++;
                        continue;
                    }

                    var wrapper = new FileSystemInfoWrapper { Info = entry };
                    try
                    {
                        if (filter is not null && !filter(wrapper))
                        {
                            coverage.FilteredFoldersSkipped++;
                            continue;
                        }

                        var info = (DirectoryInfo)entry;
                        var child = new NodeSnapshot
                        {
                            Path = info.FullName,
                            Name = info.Name,
                            IsDirectory = true,
                            LastWriteUtc = SafeTime(() => info.LastWriteTimeUtc),
                            LastAccessUtc = SafeTime(() => info.LastAccessTimeUtc),
                            Depth = parent.Depth + 1,
                            Parent = parent,
                        };
                        parent.Children.Add(child);
                        pending.Push(child);
                    }
                    catch (Exception ex) when (IsAccessException(ex))
                    {
                        coverage.InaccessibleDirectories++;
                        coverage.MarkPartial("some matching folders could not be inspected");
                    }
                }
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                coverage.InaccessibleDirectories++;
                coverage.MarkPartial("some folders could not be enumerated");
            }
        }

        return snapshots;
    }

    private static SmartCleanupOptions CreateAnalysisOptions(SmartCleanupOptions source) => new()
    {
        PriorJunk = source.PriorJunk,
        Safe = source.Safe,
        Likely = source.Likely,
        Review = source.Review,
        AggressiveMinConfidence = source.AggressiveMinConfidence,
        RecentWindowDays = source.RecentWindowDays,
        OrphanMinDormantDays = source.OrphanMinDormantDays,
        DeepInspectMinJunk = 0,
        EnableDeepInspection = true,
        TimeBudget = Timeout.InfiniteTimeSpan,
        MaxEntriesPerDirectory = source.MaxEntriesPerDirectory,
        MaxDepth = source.MaxDepth,
        DeepenMinBytes = source.DeepenMinBytes,
        DeepenMaxDepth = source.DeepenMaxDepth,
        DeepenMaxTargetsPerRound = source.DeepenMaxTargetsPerRound,
        DeepenMaxRounds = source.DeepenMaxRounds,
        UseCreationTime = source.UseCreationTime,
        MaxDegreeOfParallelism = source.MaxDegreeOfParallelism,
        PreserveAllDescendants = true,
    };

    private static ObservationDiscoveryResult EmptyResult(IEnumerable<NodeSnapshot> discovered,
        ObservationDiscoveryCoverage coverage, IObservationLookup? existing, ObservationOptions options, ISystemSnapshot machine)
    {
        var empty = new AnalysisResult
        {
            Assessments = new Dictionary<NodeSnapshot, JunkAssessment>(),
        };
        return ObservationNominator.Nominate(discovered, empty, machine, options, existing, coverage: coverage);
    }

    private static DateTime? SafeTime(Func<DateTime> read)
    {
        try
        {
            var value = read();
            return value.Year < 1700 ? null : value;
        }
        catch (Exception ex) when (IsAccessException(ex))
        {
            return null;
        }
    }

    private static bool IsAccessException(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or
        ArgumentException or NotSupportedException;

    private static string AddReason(string? previous, string next) => string.IsNullOrWhiteSpace(previous) ? next : $"{previous}; {next}";

    private sealed class MutableCoverage
    {
        public int FoldersExamined;
        public int HiddenFoldersSkipped;
        public int FilteredFoldersSkipped;
        public int ReparseFoldersSkipped;
        public int SystemFoldersSkipped;
        public int InaccessibleDirectories;
        public int DepthLimitedDirectories;
        public int EntryLimitedDirectories;
        public bool IsComplete = true;
        public bool WasCancelled;
        public string? PartialReason;
        private readonly HashSet<string> _partialReasons = new(StringComparer.OrdinalIgnoreCase);

        public void MarkPartial(string reason)
        {
            IsComplete = false;
            if (_partialReasons.Add(reason))
                PartialReason = AddReason(PartialReason, reason);
        }

        public ObservationDiscoveryCoverage ToCoverage() => new(FoldersExamined, HiddenFoldersSkipped, FilteredFoldersSkipped,
            ReparseFoldersSkipped, SystemFoldersSkipped, InaccessibleDirectories, DepthLimitedDirectories, EntryLimitedDirectories,
            IsComplete, WasCancelled, PartialReason);
    }
}
