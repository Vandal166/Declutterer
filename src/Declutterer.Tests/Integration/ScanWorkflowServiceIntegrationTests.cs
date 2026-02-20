using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Models;
using Declutterer.Services;
using Declutterer.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Declutterer.Tests.Integration;

public class ScanWorkflowServiceIntegrationTests : IDisposable
{
    private readonly TempTestDirectory _tempDir;
    private readonly ScanWorkflowService _workflowService;
    private readonly DirectoryScanService _scanService;
    private readonly TestDispatcher _testDispatcher;

    public ScanWorkflowServiceIntegrationTests()
    {
        _tempDir = new TempTestDirectory();
        _testDispatcher = new TestDispatcher();
        var filterBuilder = new ScanFilterBuilder();
        var filterService = new ScanFilterService(filterBuilder);
        _scanService = new DirectoryScanService(filterService, NullLogger<DirectoryScanService>.Instance);
        var iconLoadingService = Substitute.For<IconLoadingService>(Substitute.For<IIconLoader>());
        _workflowService = new ScanWorkflowService(_scanService, iconLoadingService, _testDispatcher);
    }

    [Fact]
    public async Task ExecuteScanAsync_EmptyDirectory_ReturnsFalse()
    {
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.False(result);
        Assert.Single(roots); // Root node added but no children
    }

    [Fact]
    public async Task ExecuteScanAsync_NonExistentDirectory_ReturnsFalse()
    {
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(Path.Combine(_tempDir.Path, "nonexistent"));
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.False(result);
        Assert.Empty(roots);
    }

    [Fact]
    public async Task ExecuteScanAsync_DirectoryWithContent_ReturnsTrue()
    {
        _tempDir.CreateFile("test.txt", 1024);
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Single(roots);
        var root = roots[0];
        Assert.True(root.IsExpanded);
        Assert.Single(root.Children);
        Assert.Equal("test.txt", root.Children[0].Name);
    }

    [Fact]
    public async Task ExecuteScanAsync_MultipleDirectories_ScansAll()
    {
        var dir1 = _tempDir.CreateSubDirectory("dir1");
        var dir2 = _tempDir.CreateSubDirectory("dir2");
        File.WriteAllText(Path.Combine(dir1, "file1.txt"), "test");
        File.WriteAllText(Path.Combine(dir2, "file2.txt"), "test");
        
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(dir1);
        scanOptions.DirectoriesToScan.Add(dir2);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Equal(2, roots.Count);
        Assert.All(roots, r => Assert.True(r.IsExpanded));
        Assert.All(roots, r => Assert.Single(r.Children));
    }

    [Fact]
    public async Task ExecuteScanAsync_WithSizeFilter_AppliesFilter()
    {
        _tempDir.CreateFile("small.txt", 512 * 1024); // 0.5 MB
        _tempDir.CreateFile("large.txt", 2 * 1024 * 1024); // 2 MB
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        Assert.Equal("large.txt", roots[0].Children[0].Name);
    }

    [Fact]
    public async Task ExecuteScanAsync_WithAgeFilter_AppliesFilter()
    {
        _tempDir.CreateFile("old.txt");
        _tempDir.SetLastModified("old.txt", DateTime.UtcNow.AddMonths(-6));
        _tempDir.CreateFile("new.txt");
        _tempDir.SetLastModified("new.txt", DateTime.UtcNow.AddDays(-1));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3) 
            }
        };
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        Assert.Equal("old.txt", roots[0].Children[0].Name);
    }

    [Fact]
    public async Task ExecuteScanAsync_WithIncludeFilesFalse_ExcludesFiles()
    {
        _tempDir.CreateFile("file.txt");
        _tempDir.CreateSubDirectory("dir");
        
        var scanOptions = new ScanOptions { IncludeFiles = false };
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        Assert.True(roots[0].Children[0].IsDirectory);
    }

    [Fact]
    public async Task ExecuteScanAsync_NestedStructure_LoadsOnlyTopLevel()
    {
        _tempDir.CreateFile("sub1/sub2/file.txt", 1024);
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Single(roots);
        Assert.Single(roots[0].Children); // Only sub1, not sub2
        Assert.Equal("sub1", roots[0].Children[0].Name);
        Assert.True(roots[0].Children[0].IsDirectory);
    }

    [Fact]
    public async Task ExecuteScanAsync_ClearsExistingRoots()
    {
        _tempDir.CreateFile("test.txt");
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>
        {
            new TreeNode { Name = "old", FullPath = "/old" }
        };

        await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.Single(roots);
        Assert.NotEqual("old", roots[0].Name);
    }

    [Fact]
    public async Task LoadChildrenParallelAsync_WithMultipleRoots_LoadsAll()
    {
        var dir1 = _tempDir.CreateSubDirectory("root1");
        var dir2 = _tempDir.CreateSubDirectory("root2");
        File.WriteAllText(Path.Combine(dir1, "file1.txt"), "test");
        File.WriteAllText(Path.Combine(dir2, "file2.txt"), "test");

        var root1 = DirectoryScanService.CreateRootNode(dir1);
        var root2 = DirectoryScanService.CreateRootNode(dir2);
        var validRoots = new System.Collections.Generic.List<TreeNode> { root1, root2 };

        var result = await _workflowService.LoadChildrenParallelAsync(validRoots, null);

        Assert.True(result);
        Assert.Single(root1.Children);
        Assert.Single(root2.Children);
        Assert.True(root1.IsExpanded);
        Assert.True(root2.IsExpanded);
    }

    [Fact]
    public async Task LoadChildrenParallelAsync_EmptyRoots_ReturnsFalse()
    {
        var root = DirectoryScanService.CreateRootNode(_tempDir.Path);
        var validRoots = new System.Collections.Generic.List<TreeNode> { root };

        var result = await _workflowService.LoadChildrenParallelAsync(validRoots, null);

        Assert.False(result);
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task ExecuteScanAsync_LargeNumberOfFiles_HandlesCorrectly()
    {
        // Create many files to test batching
        for (int i = 0; i < 250; i++)
        {
            _tempDir.CreateFile($"file{i}.txt", 1024);
        }
        
        var scanOptions = new ScanOptions();
        scanOptions.DirectoriesToScan.Add(_tempDir.Path);
        var roots = new ObservableCollection<TreeNode>();

        var result = await _workflowService.ExecuteScanAsync(scanOptions, roots);

        Assert.True(result);
        Assert.Single(roots);
        Assert.Equal(250, roots[0].Children.Count);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }
}
