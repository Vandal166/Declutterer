using System.Collections.ObjectModel;
using Declutterer.Abstractions;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Tests.Helpers;
using Declutterer.UI.ViewModels;
using NSubstitute;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Tests.ViewModels;

public class CleanupWindowViewModelTests : IDisposable
{
    private readonly TempTestDirectory _tempDir;
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IErrorDialogService _errorDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IDeleteService _deleteService;

    public CleanupWindowViewModelTests()
    {
        _tempDir = new TempTestDirectory();
        _explorerLauncher = Substitute.For<IExplorerLauncher>();
        _errorDialogService = Substitute.For<IErrorDialogService>();
        _confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        _deleteService = Substitute.For<IDeleteService>();
    }

    private CleanupWindowViewModel CreateViewModel(ObservableCollection<TreeNode> items)
        => new(items, _explorerLauncher, _errorDialogService, _confirmationDialogService, _deleteService);

    // ────────────────────────────────────────────────────────────────
    // BuildGroupedItems – categorisation correctness
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildGroupedItems_LargeDirectory_AppearsOnlyInLargeDirectories()
    {
        var dir = new TreeNode
        {
            FullPath = _tempDir.Path,
            IsDirectory = true,
            Size = 200 * 1024 * 1024, // 200 MB
            LastModified = DateTime.Now
        };

        var vm = CreateViewModel(new ObservableCollection<TreeNode> { dir });

        Assert.Contains(dir, vm.LargeDirectories);
        Assert.DoesNotContain(dir, vm.LargeFiles);
    }

    [Fact]
    public void BuildGroupedItems_LargeFile_AppearsOnlyInLargeFiles()
    {
        var filePath = _tempDir.CreateFile("bigfile.bin", 100);
        var file = new TreeNode
        {
            FullPath = filePath,
            IsDirectory = false,
            Size = 200 * 1024 * 1024, // 200 MB
            LastModified = DateTime.Now
        };

        var vm = CreateViewModel(new ObservableCollection<TreeNode> { file });

        Assert.Contains(file, vm.LargeFiles);
        Assert.DoesNotContain(file, vm.LargeDirectories);
    }

    [Fact]
    public void BuildGroupedItems_LargeDirectoryWithLargeFileChild_DirectoryAppearsOnlyInLargeDirectories()
    {
        // Both the parent directory and the large file inside it are in ItemsToDelete.
        // After GetTopLevelItems filtering only the directory remains, and it must
        // appear exclusively in LargeDirectories – never in LargeFiles.
        var subDir = _tempDir.CreateSubDirectory("bigdir");
        var filePath = _tempDir.CreateFile(Path.Combine("bigdir", "bigfile.bin"), 100);

        var dir = new TreeNode
        {
            FullPath = subDir,
            IsDirectory = true,
            Size = 500 * 1024 * 1024, // 500 MB
            LastModified = DateTime.Now
        };
        var file = new TreeNode
        {
            FullPath = filePath,
            IsDirectory = false,
            Size = 200 * 1024 * 1024, // 200 MB
            LastModified = DateTime.Now
        };

        var vm = CreateViewModel(new ObservableCollection<TreeNode> { dir, file });

        // The parent directory should appear in LargeDirectories only
        Assert.Contains(dir, vm.LargeDirectories);
        Assert.DoesNotContain(dir, vm.LargeFiles);
    }

    [Fact]
    public void BuildGroupedItems_SmallItems_AppearInOtherItems()
    {
        var filePath = _tempDir.CreateFile("small.txt", 1024);
        var file = new TreeNode
        {
            FullPath = filePath,
            IsDirectory = false,
            Size = 1024, // 1 KB – not large, not old
            LastModified = DateTime.Now
        };

        var vm = CreateViewModel(new ObservableCollection<TreeNode> { file });

        Assert.Contains(file, vm.OtherItems);
        Assert.DoesNotContain(file, vm.LargeFiles);
        Assert.DoesNotContain(file, vm.LargeDirectories);
    }

    [Fact]
    public void BuildGroupedItems_OldFile_AppearsInOldFiles()
    {
        var filePath = _tempDir.CreateFile("old.txt", 1024);
        var file = new TreeNode
        {
            FullPath = filePath,
            IsDirectory = false,
            Size = 1024,
            LastModified = DateTime.Now.AddYears(-3) // older than 2-year threshold
        };

        var vm = CreateViewModel(new ObservableCollection<TreeNode> { file });

        Assert.Contains(file, vm.OldFiles);
    }

    [Fact]
    public void BuildGroupedItems_EmptyCollection_AllGroupsAreEmpty()
    {
        var vm = CreateViewModel(new ObservableCollection<TreeNode>());

        Assert.Empty(vm.LargeDirectories);
        Assert.Empty(vm.LargeFiles);
        Assert.Empty(vm.OldFiles);
        Assert.Empty(vm.OtherItems);
    }

    public void Dispose() => _tempDir.Dispose();
}
