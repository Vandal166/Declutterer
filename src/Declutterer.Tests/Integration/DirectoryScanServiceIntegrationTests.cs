using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Scanning;
using Declutterer.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Declutterer.Tests.Integration;

public class DirectoryScanServiceIntegrationTests : IDisposable
{
    private readonly TempTestDirectory _tempDir;
    private readonly DirectoryScanService _scanService;
    private readonly ScanFilterService _filterService;

    public DirectoryScanServiceIntegrationTests()
    {
        _tempDir = new TempTestDirectory();
        var filterBuilder = new ScanFilterBuilder();
        _filterService = new ScanFilterService(filterBuilder);
        _scanService = new DirectoryScanService(_filterService, NullLogger<DirectoryScanService>.Instance);
    }

    [Fact]
    public void CreateRootNode_ValidDirectory_ReturnsValidTreeNode()
    {
        var result = DirectoryScanService.CreateRootNode(_tempDir.Path);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFileName(_tempDir.Path), result.Name);
        Assert.Equal(_tempDir.Path, result.FullPath);
        Assert.True(result.IsDirectory);
        Assert.Equal(0, result.Depth);
        Assert.Null(result.Parent);
        Assert.True(result.HasChildren);
    }

    [Fact]
    public async Task LoadChildrenAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, null);

        Assert.Empty(children);
    }

    [Fact]
    public async Task LoadChildrenAsync_DirectoryWithFiles_ReturnsFileNodes()
    {
        _tempDir.CreateFile("file1.txt", 1024);
        _tempDir.CreateFile("file2.txt", 2048);
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, null);

        Assert.Equal(2, children.Count);
        Assert.All(children, child => Assert.False(child.IsDirectory));
        Assert.Contains(children, c => c.Name == "file1.txt" && c.Size == 1024);
        Assert.Contains(children, c => c.Name == "file2.txt" && c.Size == 2048);
    }

    [Fact]
    public async Task LoadChildrenAsync_DirectoryWithSubdirectories_ReturnsDirectoryNodes()
    {
        _tempDir.CreateSubDirectory("dir1");
        _tempDir.CreateSubDirectory("dir2");
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, null);

        Assert.Equal(2, children.Count);
        Assert.All(children, child => Assert.True(child.IsDirectory));
        Assert.Contains(children, c => c.Name == "dir1");
        Assert.Contains(children, c => c.Name == "dir2");
    }

    [Fact]
    public async Task LoadChildrenAsync_MixedContent_ReturnsBothFilesAndDirectories()
    {
        _tempDir.CreateFile("file.txt", 1024);
        _tempDir.CreateSubDirectory("dir");
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, null);

        Assert.Equal(2, children.Count);
        Assert.Single(children, c => c.IsDirectory);
        Assert.Single(children, c => !c.IsDirectory);
    }

    [Fact]
    public async Task LoadChildrenAsync_WithSizeFilter_FiltersSmallFiles()
    {
        _tempDir.CreateFile("small.txt", 512 * 1024); // 0.5 MB
        _tempDir.CreateFile("large.txt", 2 * 1024 * 1024); // 2 MB
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.Equal("large.txt", children[0].Name);
    }

    [Fact]
    public async Task LoadChildrenAsync_WithModifiedDateFilter_FiltersNewFiles()
    {
        _tempDir.CreateFile("old.txt");
        _tempDir.SetLastModified("old.txt", DateTime.UtcNow.AddMonths(-6));
        _tempDir.CreateFile("new.txt");
        _tempDir.SetLastModified("new.txt", DateTime.UtcNow.AddDays(-1));
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3) 
            }
        };

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.Equal("old.txt", children[0].Name);
    }

    [Fact]
    public async Task LoadChildrenAsync_NestedStructure_LoadsCorrectly()
    {
        _tempDir.CreateFile("subdir/nested/file.txt", 1024);
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, null);

        Assert.Single(children);
        var subdir = children[0];
        Assert.Equal("subdir", subdir.Name);
        Assert.True(subdir.IsDirectory);
        Assert.Equal(1, subdir.Depth);
        Assert.Equal(rootNode, subdir.Parent);
    }

    [Fact]
    public async Task LoadChildrenAsync_PreservesLastModifiedDate()
    {
        var expectedDate = DateTime.UtcNow.AddMonths(-2);
        _tempDir.CreateFile("test.txt");
        _tempDir.SetLastModified("test.txt", expectedDate);
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, null);

        Assert.Single(children);
        var file = children[0];
        Assert.True(Math.Abs((file.LastModified!.Value - expectedDate).TotalSeconds) < 1);
    }

    [Fact]
    public async Task LoadChildrenAsync_WithIncludeFilesFalse_ExcludesFiles()
    {
        _tempDir.CreateFile("file.txt", 1024);
        _tempDir.CreateSubDirectory("dir");
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);
        
        var scanOptions = new ScanOptions { IncludeFiles = false };

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.True(children[0].IsDirectory);
        Assert.Equal("dir", children[0].Name);
    }

    [Fact]
    public void CalculateDirectorySize_EmptyDirectory_ReturnsZero()
    {
        var dirInfo = new DirectoryInfo(_tempDir.Path);

        var size = DirectoryScanService.CalculateDirectorySize(dirInfo);

        Assert.Equal(0, size);
    }

    [Fact]
    public void CalculateDirectorySize_DirectoryWithFiles_ReturnsCorrectSize()
    {
        _tempDir.CreateFile("file1.txt", 1024);
        _tempDir.CreateFile("file2.txt", 2048);
        var dirInfo = new DirectoryInfo(_tempDir.Path);

        var size = DirectoryScanService.CalculateDirectorySize(dirInfo);

        Assert.Equal(3072, size);
    }

    [Fact]
    public void CalculateDirectorySize_NestedStructure_ReturnsCorrectTotalSize()
    {
        _tempDir.CreateFile("root.txt", 1024);
        _tempDir.CreateFile("sub1/file1.txt", 2048);
        _tempDir.CreateFile("sub1/sub2/file2.txt", 4096);
        var dirInfo = new DirectoryInfo(_tempDir.Path);

        var size = DirectoryScanService.CalculateDirectorySize(dirInfo);

        Assert.Equal(7168, size);
    }

    [Fact]
    public async Task LoadChildrenForMultipleRootsAsync_MultipleRoots_LoadsAllCorrectly()
    {
        var dir1 = _tempDir.CreateSubDirectory("root1");
        var dir2 = _tempDir.CreateSubDirectory("root2");
        File.WriteAllText(Path.Combine(dir1, "file1.txt"), "test");
        File.WriteAllText(Path.Combine(dir2, "file2.txt"), "test");

        var root1 = DirectoryScanService.CreateRootNode(dir1);
        var root2 = DirectoryScanService.CreateRootNode(dir2);
        var roots = new[] { root1, root2 };

        var childrenByRoot = await _scanService.LoadChildrenForMultipleRootsAsync(roots, null);

        Assert.Equal(2, childrenByRoot.Count);
        Assert.Single(childrenByRoot[root1]);
        Assert.Single(childrenByRoot[root2]);
        Assert.Equal("file1.txt", childrenByRoot[root1][0].Name);
        Assert.Equal("file2.txt", childrenByRoot[root2][0].Name);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }
}
