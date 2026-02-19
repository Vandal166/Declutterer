using Declutterer.ViewModels;
using Declutterer.Models;
using Declutterer.Abstractions;
using NSubstitute;

namespace Declutterer.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private readonly INavigationService _navigationService;
    private readonly IScanWorkflowService _scanWorkflowService;
    private readonly ITreeNavigationService _treeNavigationService;
    private readonly IContextMenuService _contextMenuService;
    private readonly ICommandService _commandService;
    private readonly IClipboardService _clipboardService;
    private readonly ISelectionManagementService _selectionManagementService;

    public MainWindowViewModelTests()
    {
        // Setup mocks for all dependencies
        _navigationService = Substitute.For<INavigationService>();
        _scanWorkflowService = Substitute.For<IScanWorkflowService>();
        _treeNavigationService = Substitute.For<ITreeNavigationService>();
        _contextMenuService = Substitute.For<IContextMenuService>();
        _commandService = Substitute.For<ICommandService>();
        _clipboardService = Substitute.For<IClipboardService>();
        _selectionManagementService = Substitute.For<ISelectionManagementService>();
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            _navigationService,
            _scanWorkflowService,
            _treeNavigationService,
            _contextMenuService,
            _commandService,
            _clipboardService,
            _selectionManagementService
        );
    }

    [Fact]
    public void Constructor_InitializesCollections()
    {
        var viewModel = CreateViewModel();
        
        Assert.NotNull(viewModel.Roots);
        Assert.Empty(viewModel.Roots);
        Assert.NotNull(viewModel.SelectedNodes);
        Assert.Empty(viewModel.SelectedNodes);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var viewModel = CreateViewModel();
        
        Assert.False(viewModel.IsAnyNodeLoading);
        Assert.False(viewModel.NoChildrenFound);
        Assert.Equal(string.Empty, viewModel.SelectedNodesSizeText);
        Assert.False(viewModel.IsExpandingAll);
    }

    [Fact]
    public void IsTreeDataGridVisible_WhenNoRoots_ReturnsFalse()
    {
        var viewModel = CreateViewModel();
        
        Assert.False(viewModel.IsTreeDataGridVisible);
    }

    [Fact]
    public void IsTreeDataGridVisible_WhenHasRootsAndNoChildrenNotFound_ReturnsTrue()
    {
        var viewModel = CreateViewModel();
        viewModel.Roots.Add(new TreeNode { Name = "Root" });
        
        Assert.True(viewModel.IsTreeDataGridVisible);
    }

    [Fact]
    public void IsTreeDataGridVisible_WhenHasRootsButNoChildrenFound_ReturnsFalse()
    {
        var viewModel = CreateViewModel();
        viewModel.Roots.Add(new TreeNode { Name = "Root" });
        viewModel.NoChildrenFound = true;
        
        Assert.False(viewModel.IsTreeDataGridVisible);
    }

    [Fact]
    public void Roots_WhenItemAdded_NotifiesIsTreeDataGridVisibleChanged()
    {
        var viewModel = CreateViewModel();
        var propertyChangedRaised = false;
        
        viewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsTreeDataGridVisible))
            {
                propertyChangedRaised = true;
            }
        };
        
        viewModel.Roots.Add(new TreeNode { Name = "Root" });
        
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void SelectedNodes_WhenItemAdded_UpdatesSelectedNodesSizeText()
    {
        var viewModel = CreateViewModel();
        var node = new TreeNode { Size = 1024 * 1024 }; // 1 MB
        
        viewModel.SelectedNodes.Add(node);
        
        Assert.Equal("1 MB", viewModel.SelectedNodesSizeText);
    }

    [Fact]
    public void SelectedNodes_WhenMultipleItemsAdded_SumsUpSizes()
    {
        var viewModel = CreateViewModel();
        var node1 = new TreeNode { Size = 1024 * 1024 }; // 1 MB
        var node2 = new TreeNode { Size = 2 * 1024 * 1024 }; // 2 MB
        
        viewModel.SelectedNodes.Add(node1);
        viewModel.SelectedNodes.Add(node2);
        
        Assert.Equal("3 MB", viewModel.SelectedNodesSizeText);
    }

    [Fact]
    public void SelectedNodes_WhenItemRemoved_UpdatesSelectedNodesSizeText()
    {
        var viewModel = CreateViewModel();
        var node1 = new TreeNode { Size = 1024 * 1024 }; // 1 MB
        var node2 = new TreeNode { Size = 2 * 1024 * 1024 }; // 2 MB
        
        viewModel.SelectedNodes.Add(node1);
        viewModel.SelectedNodes.Add(node2);
        viewModel.SelectedNodes.Remove(node1);
        
        Assert.Equal("2 MB", viewModel.SelectedNodesSizeText);
    }

    [Fact]
    public void SelectedNodes_WhenCleared_ResetsSelectedNodesSizeText()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNodes.Add(new TreeNode { Size = 1024 * 1024 });
        
        viewModel.SelectedNodes.Clear();
        
        Assert.Equal("0B", viewModel.SelectedNodesSizeText);
    }

    [Fact]
    public async Task LoadChildrenForNodeAsync_SetsIsAnyNodeLoadingDuringOperation()
    {
        var viewModel = CreateViewModel();
        var node = new TreeNode { Name = "Test" };
        var loadingDuringCall = false;

        _treeNavigationService.LoadChildrenForNodeAsync(node, Arg.Any<ScanOptions?>())
            .Returns(callInfo =>
            {
                loadingDuringCall = viewModel.IsAnyNodeLoading;
                return Task.CompletedTask;
            });

        await viewModel.LoadChildrenForNodeAsync(node);

        Assert.True(loadingDuringCall);
        Assert.False(viewModel.IsAnyNodeLoading); // Should be reset after completion
    }

    [Fact]
    public async Task LoadChildrenForNodeAsync_CallsTreeNavigationService()
    {
        var viewModel = CreateViewModel();
        var node = new TreeNode { Name = "Test" };

        await viewModel.LoadChildrenForNodeAsync(node);

        await _treeNavigationService.Received(1).LoadChildrenForNodeAsync(node, Arg.Any<ScanOptions?>());
    }

    [Fact]
    public async Task LoadChildrenForNodeAsync_ResetsIsAnyNodeLoadingEvenOnException()
    {
        var viewModel = CreateViewModel();
        var node = new TreeNode { Name = "Test" };

        _treeNavigationService.LoadChildrenForNodeAsync(node, Arg.Any<ScanOptions?>())
            .Returns(Task.FromException(new Exception("Test exception")));

        try
        {
            await viewModel.LoadChildrenForNodeAsync(node);
        }
        catch
        {
            // Expected
        }

        Assert.False(viewModel.IsAnyNodeLoading);
    }

    [Fact]
    public void Dispose_CanBeCalledSafely()
    {
        var viewModel = CreateViewModel();
        
        viewModel.Dispose();
        
        // No exception should be thrown
        Assert.True(true);
    }
}
