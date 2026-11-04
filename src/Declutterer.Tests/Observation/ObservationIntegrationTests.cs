using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;
using Declutterer.UI.Services.Workflow;
using Declutterer.Utilities.Helpers;

namespace Declutterer.Tests.Observation;

/// <summary>SmartCleanupService and ObservationReassessor with the real engine, real folders and the real SQLite ledger.</summary>
public class ObservationIntegrationTests : IDisposable
{
    private readonly CheckInRig _r = new(defaultSources: false);
    private readonly ObservationOptions _o = new() { Enabled = true, ObserveMinBytes = 0 };
    private readonly string _user;

    public ObservationIntegrationTests()
    {
        _user = Path.Combine(_r.Dir.Path, "user");
        Directory.CreateDirectory(Path.Combine(_user, "AppData", "Roaming"));
        _r.Machine.HasInstalledAppInventory = true;
        _r.Machine.AppDataRoots.Add(Path.Combine(_user, "AppData", "Roaming"));
        _r.Machine.ContainerRoots.Add(_user);
        _r.Machine.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
    }

    public void Dispose() => _r.Dispose();

    private string OrphanApp(string name = "OldTool")
    {
        var rel = $"user/AppData/Roaming/{name}";
        _r.Dir.CreateFile($"{rel}/settings.json", 3000);
        _r.Dir.CreateFile($"{rel}/session.dat", 9000);
        var old = DateTime.UtcNow.AddDays(-800);
        foreach (var f in Directory.EnumerateFiles(_r.Item(rel))) File.SetLastWriteTimeUtc(f, old);
        foreach (var d in new[] { _r.Item(rel), Path.Combine(_user, "AppData", "Roaming"), Path.Combine(_user, "AppData"), _user }) Directory.SetLastWriteTimeUtc(d, old);
        return _r.Item(rel);
    }

    /// <summary>Loads the folder tree into TreeNodes like the scan would (every level).</summary>
    private static TreeNode Load(string path, TreeNode? parent)
    {
        var info = new DirectoryInfo(path);
        var node = new TreeNode
        {
            Name = info.Name, FullPath = path, IsDirectory = true, LastModified = info.LastWriteTimeUtc,
            Size = info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length),
            Depth = parent is null ? 0 : parent.Depth + 1, Parent = parent, HasChildren = info.EnumerateFileSystemInfos().Any(),
        };
        foreach (var dir in info.EnumerateDirectories())
            node.Children.Add(Load(dir.FullName, node));
        foreach (var file in info.EnumerateFiles())
            node.Children.Add(new TreeNode { Name = file.Name, FullPath = file.FullName, Size = file.Length, LastModified = file.LastWriteTimeUtc, Depth = node.Depth + 1, Parent = node });
        return node;
    }

    private sealed class NoTreeNavigation : ITreeNavigationService
    {
        public Task LoadChildrenForNodeAsync(TreeNode node, ScanOptions? currentScanOptions) => Task.CompletedTask;
        public Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, bool isRoot = false, ScanOptions? currentScanOptions = null) => Task.CompletedTask;
    }

    private sealed class FakeCommands : ICommandService
    {
        public void SelectAllChildren(IEnumerable<TreeNode> roots) => throw new NotSupportedException();
        public void DeselectAll(ObservableHashSet<TreeNode> selectedNodes) { foreach (var n in selectedNodes.ToList()) n.IsCheckboxSelected = false; selectedNodes.Clear(); }
        public void SmartSelect(IEnumerable<TreeNode> roots, ScanOptions? scanOptions, ObservableHashSet<TreeNode> selectedNodes) => throw new NotSupportedException();
    }

    private SmartCleanupService Service(ObservationOptions? options = null, IObservationLedger? ledger = null, SmartCleanupOptions? cleanupOptions = null) => new(
        new JunkAssessmentEngine(options ?? _o), new SmartCleanupSelector(), new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow),
        new NoTreeNavigation(), new FakeCommands(), new TestDispatcher(),
        cleanupOptions ?? new SmartCleanupOptions { UseCreationTime = false, MaxDegreeOfParallelism = 2, DeepenMaxRounds = 0 },
        ledger ?? _r.Ledger, options ?? _o, _r.Time);

    private ObservationReassessor Reassessor() => new(new JunkAssessmentEngine(_o), new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow), _r.Ledger,
        _r.Walker, _o, new SmartCleanupOptions { UseCreationTime = false, MaxDegreeOfParallelism = 2 }, _r.Time);

    // ── SmartCleanupService ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SmartSelect_OffersWatchCandidates_OnlyWhenTheFeatureIsOn()
    {
        OrphanApp();
        var root = Load(_user, null);

        var off = await Service(new ObservationOptions { Enabled = false, ObserveMinBytes = 0 }).SelectAsync(new[] { root }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());
        Assert.Null(off.ObservationCandidates);

        var on = await Service().SelectAsync(new[] { root }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());
        var c = Assert.Single(on.ObservationCandidates!);
        Assert.EndsWith("OldTool", c.Path);
        Assert.Equal(ObservationClass.AppData, c.Class);
        Assert.Equal(0, on.SelectedCount); // Review only: nothing ticked in Balanced
    }

    [Fact]
    public async Task WatchDiscoveryFindsDeepFolders_AndDoesNotDependOnWhatTheUiTreeLoaded()
    {
        var path = OrphanApp();
        var scanRoot = Path.Combine(_user, "AppData", "Roaming");
        var shallowRoot = new TreeNode { Name = "Roaming", FullPath = scanRoot, IsDirectory = true, HasChildren = true };
        var deepRoot = Load(scanRoot, null);

        var shallow = await Service().SelectAsync(new[] { shallowRoot }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());
        var deep = await Service().SelectAsync(new[] { deepRoot }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());

        var shallowCandidate = Assert.Single(shallow.ObservationCandidates!);
        var deepCandidate = Assert.Single(deep.ObservationCandidates!);
        Assert.Equal(PathKey.Normalize(path), PathKey.Normalize(shallowCandidate.Path));
        Assert.Equal(PathKey.Normalize(shallowCandidate.Path), PathKey.Normalize(deepCandidate.Path));
        Assert.Equal(shallow.ObservationDiscovery!.FoldersExamined, deep.ObservationDiscovery!.FoldersExamined);
        Assert.Equal(shallow.ObservationDiscovery.CandidatesFound, deep.ObservationDiscovery.CandidatesFound);
        Assert.True(shallow.ObservationDiscovery.IsComplete);
    }

    [Fact]
    public async Task AggressiveSelectionDoesNotAlsoOfferTheSelectedFolderForWatching()
    {
        var path = OrphanApp(); // Review with enough confidence for Aggressive mode.
        var root = Load(_user, null);
        var selected = new ObservableHashSet<TreeNode>();

        var result = await Service(cleanupOptions: new SmartCleanupOptions
        {
            UseCreationTime = false, MaxDegreeOfParallelism = 2, DeepenMaxRounds = 0, AggressiveMinConfidence = 0.5,
        }).SelectAsync(new[] { root }, SmartSelectMode.Aggressive, null, selected);

        Assert.Contains(selected, node => string.Equals(PathKey.Normalize(node.FullPath), PathKey.Normalize(path), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.ObservationCandidates!, candidate =>
            string.Equals(PathKey.Normalize(candidate.Path), PathKey.Normalize(path), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.ObservationDiscovery!.ExcludedSelectedOverlap);
    }

    [Fact]
    public async Task AScopeChangeDuringDiscoveryDoesNotApplyTheOldSelectionPlan()
    {
        OrphanApp();
        var selected = new ObservableHashSet<TreeNode>();
        var scopeCurrent = true;
        var progress = new ChangeScopeOnDiscoveryProgress(() => scopeCurrent = false);

        var result = await Service(cleanupOptions: new SmartCleanupOptions
        {
            UseCreationTime = false, MaxDegreeOfParallelism = 2, DeepenMaxRounds = 0, AggressiveMinConfidence = 0.5,
        }).SelectAsync(new[] { Load(_user, null) }, SmartSelectMode.Aggressive, null, selected, progress: progress,
            isCurrentScope: () => scopeCurrent);

        Assert.False(scopeCurrent);
        Assert.Empty(selected);
        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(1, result.ObservationDiscovery!.ExcludedSelectedOverlap);
    }

    [Fact]
    public async Task DiscoveryHonorsDirectoryScanFilters_AndReportsTheExclusion()
    {
        OrphanApp();
        var options = new ScanOptions
        {
            DirectorySizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 10 },
        };
        var root = new TreeNode { Name = "user", FullPath = _user, IsDirectory = true, HasChildren = true };

        var result = await Service().SelectAsync(new[] { root }, SmartSelectMode.Balanced, options, new ObservableHashSet<TreeNode>());

        Assert.Empty(result.ObservationCandidates!);
        Assert.True(result.ObservationDiscovery!.FilteredFoldersSkipped > 0);
        Assert.True(result.ObservationDiscovery.IsComplete);
    }

    [Fact]
    public async Task InaccessibleOrDepthLimitedCoverageIsReportedAsPartial()
    {
        var missingRoot = new TreeNode { Name = "missing", FullPath = Path.Combine(_r.Dir.Path, "missing"), IsDirectory = true };
        var missing = await Service().SelectAsync(new[] { missingRoot }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());
        Assert.False(missing.ObservationDiscovery!.IsComplete);
        Assert.True(missing.ObservationDiscovery.InaccessibleDirectories > 0);

        OrphanApp();
        _r.Dir.CreateFile("user/Projects/first/deep/file.bin", 100);
        _r.Dir.CreateFile("user/Projects/second/deep/file.bin", 100);
        var options = new SmartCleanupOptions { UseCreationTime = false, MaxDegreeOfParallelism = 2, DeepenMaxRounds = 0, MaxDepth = 1 };
        var shallow = new SmartCleanupService(new JunkAssessmentEngine(_o), new SmartCleanupSelector(),
            new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow), new NoTreeNavigation(), new FakeCommands(), new TestDispatcher(),
            options, _r.Ledger, _o, _r.Time);
        var limited = await shallow.SelectAsync(new[] { new TreeNode { Name = "user", FullPath = _user, IsDirectory = true } },
            SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());
        Assert.False(limited.ObservationDiscovery!.IsComplete);
        Assert.True(limited.ObservationDiscovery.DepthLimitedDirectories > 1);
        Assert.Equal(1, limited.ObservationDiscovery.PartialReason!.Split("the configured directory depth limit was reached").Length - 1);
    }

    [Fact]
    public async Task DiscoveryCancellationReturnsAnExplicitPartialResult()
    {
        OrphanApp();
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnDiscoveryProgress(cancellation);
        var result = await Service().SelectAsync(new[]
            { new TreeNode { Name = "user", FullPath = _user, IsDirectory = true } }, SmartSelectMode.Balanced, null,
            new ObservableHashSet<TreeNode>(), cancellation.Token, progress);

        Assert.NotNull(result.ObservationDiscovery);
        Assert.True(result.ObservationDiscovery!.WasCancelled);
        Assert.False(result.ObservationDiscovery.IsComplete);
        Assert.Contains("cancelled", result.ObservationDiscovery.PartialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HiddenFoldersFollowTheScanSettingAndAreCountedWhenExcluded()
    {
        if (!OperatingSystem.IsWindows())
            return;

        OrphanApp();
        var appData = Path.Combine(_user, "AppData");
        File.SetAttributes(appData, File.GetAttributes(appData) | FileAttributes.Hidden);
        try
        {
            var root = new TreeNode { Name = "user", FullPath = _user, IsDirectory = true };
            var excluded = await Service().SelectAsync(new[] { root }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());
            Assert.Empty(excluded.ObservationCandidates!);
            Assert.True(excluded.ObservationDiscovery!.HiddenFoldersSkipped > 0);

            var included = await Service().SelectAsync(new[] { root }, SmartSelectMode.Balanced,
                new ScanOptions { IncludeHidden = true }, new ObservableHashSet<TreeNode>());
            Assert.Contains(included.ObservationCandidates!, c => c.Path.EndsWith("OldTool", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(appData, File.GetAttributes(appData) & ~FileAttributes.Hidden);
        }
    }

    private sealed class CancelOnDiscoveryProgress(CancellationTokenSource cancellation) : IProgress<SmartCleanupProgress>
    {
        public void Report(SmartCleanupProgress value)
        {
            if (value.IsObservationDiscovery && value.Stage == "Discovering folders")
                cancellation.Cancel();
        }
    }

    private sealed class ChangeScopeOnDiscoveryProgress(Action changeScope) : IProgress<SmartCleanupProgress>
    {
        public void Report(SmartCleanupProgress value)
        {
            if (value.IsObservationDiscovery && value.Stage == "Discovering folders")
                changeScope();
        }
    }

    [Fact]
    public async Task SmartSelect_UsesTheLedger_ACompletedQuietWindowLiftsTheItemIntoBalanced()
    {
        var path = OrphanApp();
        var entry = ObservationTestKit.Entry(path, _r.Time.UtcNow.AddDays(-60), ObservationClass.AppData, 28) with
        {
            State = WatchState.QuietEligible, CoveredDays = 28, ValidUntilUtc = _r.Time.UtcNow.AddDays(20),
        };
        await _r.Ledger.EnrollAsync(entry);
        var root = Load(_user, null);
        var selected = new ObservableHashSet<TreeNode>();

        var result = await Service().SelectAsync(new[] { root }, SmartSelectMode.Balanced, null, selected);

        var tool = root.Children.Single(c => c.Name == "AppData").Children.Single(c => c.Name == "Roaming").Children.Single(c => c.Name == "OldTool");
        Assert.Equal(new[] { tool }, selected.ToArray());
        Assert.Contains("Observed unused", tool.Assessment!.Explain());
        Assert.Empty(result.ObservationCandidates!); // already watched: not offered again
    }

    [Fact]
    public async Task SmartSelect_AndTheLedger_AreAsBeforeWhenTheLedgerIsBroken()
    {
        OrphanApp();
        var broken = new SqliteObservationLedger(Path.Combine(_r.Dir.Path, "blocked"));
        Directory.CreateDirectory(Path.Combine(_r.Dir.Path, "blocked")); // a directory where the database file should be
        var root = Load(_user, null);

        var result = await Service(ledger: broken).SelectAsync(new[] { root }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());

        Assert.Equal(0, result.SelectedCount); // no evidence, no promotion, no crash
    }

    [Fact]
    public async Task SmartSelect_RecordsItselfAsOwnActivity()
    {
        OrphanApp();

        await Service().SelectAsync(new[] { Load(_user, null) }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());

        Assert.Contains(await _r.Ledger.GetOwnActivityAsync(_r.Time.UtcNow.AddDays(-1)), i => i.Kind == "smart-select");
    }

    [Fact]
    public async Task WithoutALedger_SmartSelectIsExactlyWhatItWas()
    {
        OrphanApp();
        var service = new SmartCleanupService(new JunkAssessmentEngine(), new SmartCleanupSelector(), new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow),
            new NoTreeNavigation(), new FakeCommands(), new TestDispatcher(), new SmartCleanupOptions { UseCreationTime = false, DeepenMaxRounds = 0 });

        var result = await service.SelectAsync(new[] { Load(_user, null) }, SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());

        Assert.Null(result.ObservationCandidates);
    }

    // ── reassessment ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassess_RunsTheFullEngineOnOneItem_AndStoresTheVerdict()
    {
        var path = OrphanApp();
        var entry = (await _r.Ledger.EnrollAsync(ObservationTestKit.Entry(path, _r.Time.UtcNow.AddDays(-60), ObservationClass.AppData, 28) with
        {
            State = WatchState.QuietEligible, CoveredDays = 28, ValidUntilUtc = _r.Time.UtcNow.AddDays(20),
        }))!;

        var result = await Reassessor().ReassessAsync(entry);

        Assert.NotNull(result);
        Assert.Equal(CleanupTier.Likely, result!.Tier);
        Assert.Contains("Observed unused", result.Reasons);
        var stored = await _r.Ledger.GetAsync(entry.Id);
        Assert.Equal(CleanupTier.Likely, stored!.LastTier);
        Assert.Equal(_r.Time.UtcNow, stored.LastAssessedUtc);
        Assert.Contains("Observed unused", stored.LastReasons);
    }

    [Fact]
    public async Task Reassess_WithoutQuietEvidence_StaysReview()
    {
        var path = OrphanApp();
        var entry = (await _r.Ledger.EnrollAsync(ObservationTestKit.Entry(path, _r.Time.UtcNow, ObservationClass.AppData, 28)))!; // just started watching

        var result = await Reassessor().ReassessAsync(entry);

        Assert.Equal(CleanupTier.Review, result!.Tier);
    }

    [Fact]
    public async Task Reassess_OfAnItemThatIsGone_YieldsNothing()
    {
        var entry = ObservationTestKit.Entry(Path.Combine(_user, "nope"), _r.Time.UtcNow);

        Assert.Null(await Reassessor().ReassessAsync(entry));
    }
}
