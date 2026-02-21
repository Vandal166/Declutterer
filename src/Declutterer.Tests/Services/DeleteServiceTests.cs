using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Tests.Helpers;
using NSubstitute;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Tests.Services;

public class DeleteServiceTests : IDisposable
{
    private readonly TempTestDirectory _tempDir;
    private readonly IDeletionHistoryRepository _historyRepository;
    private readonly DeleteService _service;

    public DeleteServiceTests()
    {
        _tempDir = new TempTestDirectory();
        _historyRepository = Substitute.For<IDeletionHistoryRepository>();
        _service = new DeleteService(_historyRepository);
    }

    [Fact]
    public void Constructor_NullHistoryRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DeleteService(null!));
    }

    [Fact]
    public async Task DeletePermanentlyAsync_EmptyCollection_ReturnsSuccess()
    {
        var items = new ObservableCollection<TreeNode>();

        var result = await _service.DeletePermanentlyAsync(items);

        Assert.True(result.Success);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.TotalBytesFreed);
    }

    [Fact]
    public async Task DeletePermanentlyAsync_NonExistentPath_RecordsFailureWithoutThrowing()
    {
        var node = new TreeNode
        {
            Name = "ghost.txt",
            FullPath = Path.Combine(_tempDir.Path, "ghost.txt"),
            Size = 100
        };
        var items = new ObservableCollection<TreeNode> { node };

        var result = await _service.DeletePermanentlyAsync(items);

        Assert.False(result.Success);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task DeletePermanentlyAsync_NonExistentPaths_ReportsProgressForEachItem()
    {
        // Progress is reported before the deletion attempt, so non-existent paths still trigger progress
        var items = new ObservableCollection<TreeNode>
        {
            new() { Name = "item1.txt", FullPath = Path.Combine(_tempDir.Path, "item1.txt"), Size = 100 },
            new() { Name = "item2.txt", FullPath = Path.Combine(_tempDir.Path, "item2.txt"), Size = 100 },
        };

        var progressReports = new List<DeleteProgress>();
        var progress = new Progress<DeleteProgress>(p => progressReports.Add(p));

        await _service.DeletePermanentlyAsync(items, progress);
        await Task.Delay(50); // allow progress callbacks to flush

        Assert.Equal(2, progressReports.Count);
        Assert.Equal(1, progressReports[0].ProcessedItemCount);
        Assert.Equal(2, progressReports[0].TotalItemCount);
        Assert.Equal(2, progressReports[1].ProcessedItemCount);
        Assert.Equal(100.0, progressReports[1].ProgressPercentage, 1);
    }

    [Fact]
    public async Task DeletePermanentlyAsync_PreCancelledToken_ThrowsBeforeProcessing()
    {
        var items = new ObservableCollection<TreeNode>
        {
            new() { Name = "item.txt", FullPath = Path.Combine(_tempDir.Path, "item.txt"), Size = 100 },
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.DeletePermanentlyAsync(items, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task DeletePermanentlyAsync_CriticalSystemPath_RecordsFailureWithoutThrowing()
    {
        // On each platform the path safety check blocks deletion of system-critical paths.
        // This verifies the guard works and the error is surfaced as a result, not an exception.
        var criticalPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\Windows\System32\kernel32.dll"
            : "/etc/hostname";

        var node = new TreeNode { Name = "critical", FullPath = criticalPath, Size = 0 };
        var items = new ObservableCollection<TreeNode> { node };

        var result = await _service.DeletePermanentlyAsync(items);

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task DeletePermanentlyAsync_MultipleNonExistentPaths_RecordsAllFailures()
    {
        var items = new ObservableCollection<TreeNode>
        {
            new() { Name = "missing1.txt", FullPath = Path.Combine(_tempDir.Path, "missing1.txt"), Size = 100 },
            new() { Name = "missing2.txt", FullPath = Path.Combine(_tempDir.Path, "missing2.txt"), Size = 200 },
            new() { Name = "missing3.txt", FullPath = Path.Combine(_tempDir.Path, "missing3.txt"), Size = 300 },
        };

        var result = await _service.DeletePermanentlyAsync(items);

        Assert.False(result.Success);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(3, result.FailedCount);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_EmptyCollection_ReturnsSuccess()
    {
        var items = new ObservableCollection<TreeNode>();

        var result = await _service.MoveToRecycleBinAsync(items);

        Assert.True(result.Success);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.TotalBytesFreed);
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_PreCancelledToken_ThrowsBeforeProcessing()
    {
        var items = new ObservableCollection<TreeNode>
        {
            new() { Name = "item.txt", FullPath = Path.Combine(_tempDir.Path, "item.txt"), Size = 100 },
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.MoveToRecycleBinAsync(items, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_NonExistentPath_RecordsFailureWithoutThrowing()
    {
        var node = new TreeNode
        {
            Name = "ghost.txt",
            FullPath = Path.Combine(_tempDir.Path, "ghost.txt"),
            Size = 100
        };
        var items = new ObservableCollection<TreeNode> { node };

        var result = await _service.MoveToRecycleBinAsync(items);

        Assert.False(result.Success);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_NonExistentPaths_ReportsProgressForEachItem()
    {
        // Progress is reported before the deletion attempt, so non-existent paths still trigger progress
        var items = new ObservableCollection<TreeNode>
        {
            new() { Name = "item1.txt", FullPath = Path.Combine(_tempDir.Path, "item1.txt"), Size = 100 },
            new() { Name = "item2.txt", FullPath = Path.Combine(_tempDir.Path, "item2.txt"), Size = 100 },
        };

        var progressReports = new List<DeleteProgress>();
        var progress = new Progress<DeleteProgress>(p => progressReports.Add(p));

        await _service.MoveToRecycleBinAsync(items, progress);
        await Task.Delay(50); // allow progress callbacks to flush

        Assert.Equal(2, progressReports.Count);
        Assert.Equal(1, progressReports[0].ProcessedItemCount);
        Assert.Equal(2, progressReports[0].TotalItemCount);
        Assert.Equal(2, progressReports[1].ProcessedItemCount);
        Assert.Equal(100.0, progressReports[1].ProgressPercentage, 1);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }
}

