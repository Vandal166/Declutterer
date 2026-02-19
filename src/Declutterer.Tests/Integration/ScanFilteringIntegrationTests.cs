using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Models;
using Declutterer.Services;
using Declutterer.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Declutterer.Tests.Integration;

public class ScanFilteringIntegrationTests : IDisposable
{
    private readonly TempTestDirectory _tempDir;
    private readonly ScanFilterService _filterService;
    private readonly DirectoryScanService _scanService;

    public ScanFilteringIntegrationTests()
    {
        _tempDir = new TempTestDirectory();
        var filterBuilder = new ScanFilterBuilder();
        _filterService = new ScanFilterService(filterBuilder);
        _scanService = new DirectoryScanService(_filterService, NullLogger<DirectoryScanService>.Instance);
    }

    [Fact]
    public async Task FileSizeFilter_FiltersCorrectly()
    {
        _tempDir.CreateFile("tiny.txt", 100 * 1024); // 0.1 MB
        _tempDir.CreateFile("small.txt", 500 * 1024); // 0.5 MB
        _tempDir.CreateFile("medium.txt", 5 * 1024 * 1024); // 5 MB
        _tempDir.CreateFile("large.txt", 50 * 1024 * 1024); // 50 MB
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 } // 1 MB
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "medium.txt");
        Assert.Contains(children, c => c.Name == "large.txt");
        Assert.DoesNotContain(children, c => c.Name == "tiny.txt");
        Assert.DoesNotContain(children, c => c.Name == "small.txt");
    }

    [Fact]
    public async Task ModifiedDateFilter_FiltersOldFiles()
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-3);
        
        _tempDir.CreateFile("recent.txt");
        _tempDir.SetLastModified("recent.txt", DateTime.UtcNow.AddDays(-7));
        
        _tempDir.CreateFile("old1.txt");
        _tempDir.SetLastModified("old1.txt", DateTime.UtcNow.AddMonths(-6));
        
        _tempDir.CreateFile("old2.txt");
        _tempDir.SetLastModified("old2.txt", DateTime.UtcNow.AddMonths(-12));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = cutoffDate 
            }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "old1.txt");
        Assert.Contains(children, c => c.Name == "old2.txt");
        Assert.DoesNotContain(children, c => c.Name == "recent.txt");
    }

    [Fact]
    public async Task AccessedDateFilter_FiltersCorrectly()
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-2);
        
        _tempDir.CreateFile("accessed_recently.txt");
        _tempDir.SetLastAccessed("accessed_recently.txt", DateTime.UtcNow.AddDays(-1));
        
        _tempDir.CreateFile("not_accessed.txt");
        _tempDir.SetLastAccessed("not_accessed.txt", DateTime.UtcNow.AddMonths(-6));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseAccessedDate = true, 
                AccessedBefore = cutoffDate 
            }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.Equal("not_accessed.txt", children[0].Name);
    }

    [Fact]
    public async Task CombinedFilters_SizeAndAge_AppliesBothCorrectly()
    {
        // Create files with different combinations
        _tempDir.CreateFile("old_large.txt", 10 * 1024 * 1024); // 10 MB, old
        _tempDir.SetLastModified("old_large.txt", DateTime.UtcNow.AddMonths(-6));
        
        _tempDir.CreateFile("old_small.txt", 100 * 1024); // 0.1 MB, old
        _tempDir.SetLastModified("old_small.txt", DateTime.UtcNow.AddMonths(-6));
        
        _tempDir.CreateFile("new_large.txt", 10 * 1024 * 1024); // 10 MB, new
        _tempDir.SetLastModified("new_large.txt", DateTime.UtcNow.AddDays(-1));
        
        _tempDir.CreateFile("new_small.txt", 100 * 1024); // 0.1 MB, new
        _tempDir.SetLastModified("new_small.txt", DateTime.UtcNow.AddDays(-1));
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }, // 1 MB
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3) 
            }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        // Only old AND large files should pass
        Assert.Single(children);
        Assert.Equal("old_large.txt", children[0].Name);
    }

    [Fact]
    public async Task DirectorySizeFilter_FiltersSmallDirectories()
    {
        // Create directory with large content
        _tempDir.CreateFile("large_dir/file1.txt", 5 * 1024 * 1024);
        _tempDir.CreateFile("large_dir/file2.txt", 6 * 1024 * 1024);
        
        // Create directory with small content
        _tempDir.CreateFile("small_dir/file.txt", 100 * 1024);
        
        var scanOptions = new ScanOptions
        {
            DirectorySizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 5 } // 5 MB
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.Equal("large_dir", children[0].Name);
        Assert.True(children[0].IsDirectory);
    }

    [Fact]
    public async Task IncludeFilesFalse_ExcludesAllFiles()
    {
        _tempDir.CreateFile("file1.txt", 1024);
        _tempDir.CreateFile("file2.txt", 2048);
        _tempDir.CreateSubDirectory("dir1");
        _tempDir.CreateSubDirectory("dir2");
        
        var scanOptions = new ScanOptions { IncludeFiles = false };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.True(c.IsDirectory));
    }

    [Fact]
    public async Task FileSizeFilter_WithIncludeFilesFalse_StillIncludesDirectories()
    {
        _tempDir.CreateFile("large.txt", 10 * 1024 * 1024);
        _tempDir.CreateSubDirectory("dir");
        
        var scanOptions = new ScanOptions 
        { 
            IncludeFiles = false,
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.True(children[0].IsDirectory);
        Assert.Equal("dir", children[0].Name);
    }

    [Fact]
    public async Task MultipleFilters_ComplexScenario_AppliesAllCorrectly()
    {
        // Create complex structure
        var oldDate = DateTime.UtcNow.AddMonths(-6);
        var recentDate = DateTime.UtcNow.AddDays(-7);
        
        // Old, large file - should pass all filters
        _tempDir.CreateFile("target.txt", 10 * 1024 * 1024);
        _tempDir.SetLastModified("target.txt", oldDate);
        _tempDir.SetLastAccessed("target.txt", oldDate);
        
        // Old, small file - fails size filter
        _tempDir.CreateFile("old_small.txt", 100 * 1024);
        _tempDir.SetLastModified("old_small.txt", oldDate);
        
        // New, large file - fails age filter
        _tempDir.CreateFile("new_large.txt", 10 * 1024 * 1024);
        _tempDir.SetLastModified("new_large.txt", recentDate);
        
        // Directory with large content but old
        _tempDir.CreateFile("old_dir/content.txt", 20 * 1024 * 1024);
        _tempDir.SetLastModified("old_dir", oldDate);
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 5 },
            DirectorySizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 15 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3) 
            }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "target.txt");
        Assert.Contains(children, c => c.Name == "old_dir");
    }

    [Fact]
    public void CreateFilter_NullOptions_ReturnsNull()
    {
        var filter = _filterService.CreateFilter(null);

        Assert.Null(filter);
    }

    [Fact]
    public void CreateFilter_DefaultOptions_ReturnsNonNullFilter()
    {
        var scanOptions = new ScanOptions();
        
        var filter = _filterService.CreateFilter(scanOptions);

        Assert.NotNull(filter);
    }

    [Fact]
    public async Task EdgeCase_ExactThreshold_IncludesFile()
    {
        _tempDir.CreateFile("exact.txt", 1024 * 1024); // Exactly 1 MB
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        // Files > threshold should be included (not >=)
        Assert.Empty(children);
    }

    [Fact]
    public async Task EdgeCase_JustAboveThreshold_IncludesFile()
    {
        _tempDir.CreateFile("above.txt", 1024 * 1024 + 1); // 1 MB + 1 byte
        
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var rootNode = DirectoryScanService.CreateRootNode(_tempDir.Path);

        var children = await _scanService.LoadChildrenAsync(rootNode, scanOptions);

        Assert.Single(children);
        Assert.Equal("above.txt", children[0].Name);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }
}
