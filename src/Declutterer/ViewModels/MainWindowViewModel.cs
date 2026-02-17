using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.ViewModels;

//TODO: add exclusions for Directories so they won't be scanned at all, not even shown in the tree, Persist the exclusions in some form of settings like json file

public partial class MainWindowViewModel : ViewModelBase, IDisposable, IContextMenuProvider
{
    // ObservableProperty is used to generate the property with INotifyPropertyChanged implementation which will notify the UI when the property changes
    [ObservableProperty]
    private bool _isAnyNodeLoading = false; // used for showing loading indicator on the UI
    
    [ObservableProperty]
    private bool _noChildrenFound = false; // flag to indicate that no children were found for the selected directories based on the scan options
    
    [ObservableProperty]
    private string _selectedNodesSizeText = string.Empty; // a user-friendly string representation of the total size of the currently selected nodes
    
    public bool IsExpandingAll { get; set; } = false; // Flag to prevent multiple simultaneous expand/collapse operations
    
    private readonly IContextMenuService _contextMenuService;
    private readonly ICommandService _commandService;
    private readonly INavigationService _navigationService;
    private readonly IScanWorkflowService _scanWorkflowService;
    private readonly ITreeNavigationService _treeNavigationService;
    private readonly IClipboardService _clipboardService;
    private readonly ISelectionManagementService _selectionManagementService;

    private ScanOptions? _currentScanOptions;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    
    public MainWindowViewModel(INavigationService navigationService, IScanWorkflowService scanWorkflowService, ITreeNavigationService treeNavigationService,
        IContextMenuService contextMenuService, ICommandService commandService, IClipboardService clipboardService, ISelectionManagementService selectionManagementService)
    {
        _navigationService = navigationService;
        _scanWorkflowService = scanWorkflowService;
        _treeNavigationService = treeNavigationService;
        _contextMenuService = contextMenuService;
        _commandService = commandService;
        _clipboardService = clipboardService;
        _selectionManagementService = selectionManagementService;

        // selection change tracking
        SelectedNodes.CollectionChanged += OnSelectedNodesCollectionChanged;
        
        // Subscribe to selection management service events
        _selectionManagementService.OnNodePropertyChanged += (node, args) =>
        {
            if (node is TreeNode treeNode)
            {
                _selectionManagementService.HandleNodeSelectionChanged(treeNode, SelectedNodes);
            }
        };
    }

    private void OnSelectedNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSelectedNodesSize();
    }

    public MainWindowViewModel() { } // for designer
        
    private void UpdateSelectedNodesSize()
    {
        var selectedNodesSize = SelectedNodes.Sum(n => n.Size);
        SelectedNodesSizeText = Common.ByteConverter.ToReadableString(selectedNodesSize);
    }

    // Loads subdirectories and files for a given node when it's expanded. This is called from the UI when a node is expanded.
    public async Task LoadChildrenForNodeAsync(TreeNode node)
    {
        IsAnyNodeLoading = true;
        try
        {
            await _treeNavigationService.LoadChildrenForNodeAsync(node, _currentScanOptions);
        }
        finally
        {
            IsAnyNodeLoading = false;
        }
    }
    
    [RelayCommand]
    private void ClearAll()
    {
        Roots.Clear();
        SelectedNodes.Clear();
        _currentScanOptions = null;
        NoChildrenFound = false;
    }
    
    [RelayCommand]
    private void SelectAll() => _commandService.SelectAllChildren(Roots);

    [RelayCommand]
    private void DeselectAll() => _commandService.DeselectAll(SelectedNodes);

    [RelayCommand]
    private void SmartSelect() => _commandService.SmartSelect(Roots, _currentScanOptions, SelectedNodes);

    [RelayCommand]
    private async Task ShowScanOptionsWindowAsync()
    {
        var result = await _navigationService.ShowScanOptionsAsync();
        if (result != null)
        {
            _currentScanOptions = result;

            // Clean up old subscriptions before clearing roots
            _selectionManagementService.UnsubscribeFromAllNodes();
            SelectedNodes.Clear();

            var validRoots = new List<TreeNode>();
            IsAnyNodeLoading = true;
            try
            {
                bool scanSucceeded = await _scanWorkflowService.ExecuteScanAsync(result, validRoots);
                NoChildrenFound = !scanSucceeded;

                // Re-populate Roots from validRoots that were created
                foreach (var root in validRoots)
                {
                    Roots.Add(root);
                }
            }
            finally
            {
                IsAnyNodeLoading = false;
            }
        }
    }
    public async Task HandleAltClickExpandAsync(TreeNode node, bool shouldExpand)
    {
        IsAnyNodeLoading = true;
        try
        {
            // Reset the flag
            NoChildrenFound = false;
            IsExpandingAll = true;
            // Pass isRoot=true so we skip setting IsExpanded on the clicked root node (the TreeDataGrid handles the root node's toggle via normal click processing)
            await _treeNavigationService.ToggleAllDescendantsAsync(node, shouldExpand, isRoot: true, currentScanOptions: _currentScanOptions);
        }
        finally
        {
            IsExpandingAll = false;
            IsAnyNodeLoading = false;
        }
    }
    [RelayCommand]
    private async Task ShowCleanupWindowAsync()
    {
        if (SelectedNodes.Count == 0)
            return;

        await _navigationService.ShowCleanupWindowAsync(SelectedNodes);
    }
    
    [RelayCommand]
    public void UpdateNodeSelection(SelectionUpdateRequest request)
    {
        if (request.Node.IsCheckboxSelected == request.IsCheckboxSelected)
            return; // No change, skip

        request.Node.IsCheckboxSelected = request.IsCheckboxSelected;

        if (request.IsCheckboxSelected)
        {
            SelectedNodes.Add(request.Node);
        }
        else
        {
            SelectedNodes.Remove(request.Node);
        }
    }
    
    [RelayCommand]
    private Task ContextMenuSelect(TreeNode? node)
    {
        _contextMenuService.ToggleNodeSelection(node);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ContextMenuOpenInExplorer(TreeNode? node)
    {
        await _contextMenuService.OpenInExplorerAsync(node);
    }

    [RelayCommand]
    private async Task ContextMenuCopyPath(TreeNode? node)
    {
        if (node is null)
            return;

        await _clipboardService.CopyTextAsync(node.FullPath);
    }
    
    public void SubscribeToNodeSelectionChanges(TreeNode node)
    {
        _selectionManagementService.SubscribeToNodeSelectionChanges(node);
    }
    
    private bool _disposed = false;
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Unsubscribe from SelectedNodes collection changes
            SelectedNodes.CollectionChanged -= OnSelectedNodesCollectionChanged;
        
            // Dispose selection management service
            _selectionManagementService.Dispose();
        }
        _disposed = true;
    }
}