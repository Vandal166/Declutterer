using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Tests.Helpers;
using Declutterer.UI.Services.Workflow;
using Declutterer.Utilities.Helpers;

namespace Declutterer.Tests.Cleanup;

/// <summary>
/// Runs the real service + engine + selector against a real folder tree, with a fake tree navigation that loads children
/// from disk the way the app does (one level, plus one preloaded level).
/// </summary>
public class SmartCleanupServiceTests : IDisposable
{
    private readonly TempTestDirectory _dir = new();
    private readonly DiskTreeNavigation _navigation = new();
    private readonly SmartCleanupService _service;

    public SmartCleanupServiceTests()
    {
        var options = new SmartCleanupOptions { UseCreationTime = false, MaxDegreeOfParallelism = 2 };
        _service = new SmartCleanupService(new JunkAssessmentEngine(), new SmartCleanupSelector(), new FakeSnapshotProvider(),
            _navigation, new FakeCommandService(), new TestDispatcher(), options);
    }

    public void Dispose() => _dir.Dispose();

    private void Old(string relative, long size = 1024, int ageDays = 800)
    {
        var path = _dir.CreateFile(relative, size);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
    }

    private void SettleDirectoryAges(int ageDays = 800)
    {
        foreach (var d in Directory.EnumerateDirectories(_dir.Path, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            Directory.SetLastWriteTimeUtc(d, DateTime.UtcNow.AddDays(-ageDays));
        Directory.SetLastWriteTimeUtc(_dir.Path, DateTime.UtcNow.AddDays(-ageDays));
    }

    private TreeNode LoadedRoot()
    {
        var root = DiskTreeNavigation.CreateNode(_dir.Path, null);
        DiskTreeNavigation.Fill(root); // like the initial scan: only the first level is loaded
        return root;
    }

    private static TreeNode Find(TreeNode root, string name) =>
        root.Children.Concat(root.Children.SelectMany(c => c.Children)).Concat(root.Children.SelectMany(c => c.Children).SelectMany(c => c.Children))
            .First(n => n.Name == name);

    [Fact]
    public async Task EmptyRoots_DoNothing()
    {
        var result = await _service.SelectAsync(Array.Empty<TreeNode>(), SmartSelectMode.Balanced, null, new ObservableHashSet<TreeNode>());

        Assert.Equal(SmartCleanupResult.Empty, result);
    }

    [Fact]
    public async Task LooksInsideBigFoldersThatAreNotLoadedYet_AndFindsTheJunkThere()
    {
        const long mb = 1024 * 1024;
        Old("Projects/old-app/package.json", 300);
        Old("Projects/old-app/src/index.js", 5 * mb);
        Old("Projects/old-app/node_modules/m/index.js", 6 * mb);
        Old("Misc/old/a.dat", 2048);
        SettleDirectoryAges();

        var root = LoadedRoot();
        var selected = new ObservableHashSet<TreeNode>();
        // only the first level is loaded: "Projects" has children, but they are not in the tree yet
        Assert.Empty(root.Children.First(c => c.Name == "Projects").Children);

        var result = await _service.SelectAsync(new[] { root }, SmartSelectMode.Conservative, null, selected);

        var nodeModules = Find(root, "node_modules");
        Assert.True(_navigation.LoadCalls > 0, "the service should have loaded folders to look inside");
        Assert.Equal(new[] { nodeModules }, selected.ToArray());
        Assert.True(nodeModules.IsCheckboxSelected);
        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(1, result.SafeCount);
        Assert.Equal(6 * mb, result.SelectedBytes);

        // everything it looked at is annotated; the project itself is kept and explained
        Assert.NotNull(nodeModules.Assessment);
        Assert.Equal(CleanupTier.Safe, nodeModules.Assessment!.Tier);
        Assert.True(Find(root, "src").Assessment!.Tier <= CleanupTier.Keep);
        Assert.False(Find(root, "src").IsCheckboxSelected);
        // small folders are not worth loading just to look inside
        Assert.Empty(Find(root, "Misc").Children);
        Assert.False(Find(root, "Misc").IsCheckboxSelected);
    }

    [Fact]
    public async Task ReplacesThePreviousSelection_AndNeverKeepsStaleAnnotations()
    {
        const long mb = 1024 * 1024;
        Old("Projects/old-app/package.json", 300);
        Old("Projects/old-app/node_modules/m/index.js", 6 * mb);
        SettleDirectoryAges();
        var root = LoadedRoot();
        var selected = new ObservableHashSet<TreeNode>();
        var manual = root.Children.First(c => c.Name == "Projects");
        manual.IsCheckboxSelected = true;
        selected.Add(manual);

        await _service.SelectAsync(new[] { root }, SmartSelectMode.Conservative, null, selected);

        Assert.False(manual.IsCheckboxSelected);
        Assert.DoesNotContain(manual, selected);
        Assert.Single(selected);
    }

    [Fact]
    public async Task TheModeDecidesHowMuchGetsTicked()
    {
        const long mb = 1024 * 1024;
        // an ACTIVE project: its node_modules is "likely" junk (regenerable) but not "safe" while the project is in use
        Old("Projects/active/package.json", 300, ageDays: 2);
        Old("Projects/active/node_modules/m/index.js", 6 * mb, ageDays: 2);
        // an OLD project: its node_modules is safe
        Old("Projects/old/package.json", 300);
        Old("Projects/old/node_modules/m/index.js", 6 * mb);
        Directory.SetLastWriteTimeUtc(Path.Combine(_dir.Path, "Projects/old/node_modules/m"), DateTime.UtcNow.AddDays(-800));
        Directory.SetLastWriteTimeUtc(Path.Combine(_dir.Path, "Projects/old/node_modules"), DateTime.UtcNow.AddDays(-800));
        Directory.SetLastWriteTimeUtc(Path.Combine(_dir.Path, "Projects/old"), DateTime.UtcNow.AddDays(-800));
        Directory.SetLastWriteTimeUtc(Path.Combine(_dir.Path, "Projects"), DateTime.UtcNow.AddDays(-1));

        var conservative = new ObservableHashSet<TreeNode>();
        await _service.SelectAsync(new[] { LoadedRoot() }, SmartSelectMode.Conservative, null, conservative);

        var balanced = new ObservableHashSet<TreeNode>();
        await _service.SelectAsync(new[] { LoadedRoot() }, SmartSelectMode.Balanced, null, balanced);

        Assert.Single(conservative);
        Assert.Equal(2, balanced.Count);
        Assert.All(balanced, n => Assert.Equal("node_modules", n.Name));
    }

    // ── fakes ──────────────────────────────────────────────────────────────

    private sealed class FakeSnapshotProvider : ISystemSnapshotProvider
    {
        public Task<ISystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ISystemSnapshot>(new SystemSnapshot(new SystemSnapshotData { HasInstalledAppInventory = true }));
    }

    private sealed class FakeCommandService : ICommandService
    {
        public void SelectAllChildren(IEnumerable<TreeNode> roots) => throw new NotSupportedException();

        public void DeselectAll(ObservableHashSet<TreeNode> selectedNodes)
        {
            foreach (var n in selectedNodes.ToList())
                n.IsCheckboxSelected = false;
            selectedNodes.Clear();
        }

        public void SmartSelect(IEnumerable<TreeNode> roots, ScanOptions? scanOptions, ObservableHashSet<TreeNode> selectedNodes) =>
            throw new NotSupportedException();
    }

    private sealed class DiskTreeNavigation : ITreeNavigationService
    {
        public int LoadCalls;

        public Task LoadChildrenForNodeAsync(TreeNode node, ScanOptions? currentScanOptions)
        {
            Interlocked.Increment(ref LoadCalls);
            if (node.Children.Count > 0)
                return Task.CompletedTask;

            Fill(node);
            foreach (var child in node.Children.Where(c => c.IsDirectory && c.HasChildren))
                Fill(child); // the real service preloads one level ahead as well
            return Task.CompletedTask;
        }

        public Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, bool isRoot = false, ScanOptions? currentScanOptions = null) =>
            throw new NotSupportedException();

        public static TreeNode CreateNode(string path, TreeNode? parent)
        {
            var isDir = Directory.Exists(path);
            return new TreeNode
            {
                Name = System.IO.Path.GetFileName(path),
                FullPath = path,
                IsDirectory = isDir,
                Size = isDir ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : new FileInfo(path).Length,
                LastModified = isDir ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path),
                Depth = parent is null ? 0 : parent.Depth + 1,
                Parent = parent,
                HasChildren = isDir && Directory.EnumerateFileSystemEntries(path).Any(),
            };
        }

        public static void Fill(TreeNode node)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(node.FullPath))
                node.Children.Add(CreateNode(entry, node));
        }
    }
}
