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
    private bool _isUpdatingSelection = false; // Guard against re-entrancy during recursive selection updates

    private ScanOptions? _currentScanOptions;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    private readonly HashSet<TreeNode> _subscribedNodes = new();
    private readonly Dictionary<TreeNode, PropertyChangedEventHandler> _nodePropertyHandlers = new();
    
    public MainWindowViewModel(INavigationService navigationService, IScanWorkflowService scanWorkflowService, ITreeNavigationService treeNavigationService,
        IContextMenuService contextMenuService, ICommandService commandService, IClipboardService clipboardService)
    {
        _navigationService = navigationService;
        _scanWorkflowService = scanWorkflowService;
        _treeNavigationService = treeNavigationService;
        _contextMenuService = contextMenuService;
        _commandService = commandService;
        _clipboardService = clipboardService;

        // selection change tracking
        SelectedNodes.CollectionChanged += OnSelectedNodesCollectionChanged;
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
            UnsubscribeFromAllNodes();
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
    
    /// <summary>
    /// Called whenever a TreeNode's IsSelected property changes.
    /// </summary>
    private void OnTreeNodeSelectionChanged(TreeNode node)
    {
        // Prevent re-entrancy when we're programmatically updating children
        if (_isUpdatingSelection)
            return;

        // Update SelectedNodes collection
        if (node.IsCheckboxSelected)
        {
            SelectedNodes.Add(node);
            
            // removing all descendants from SelectedNodes since parent selection encompasses them
            RemoveDescendantsFromSelectedNodes(node);
            
            // But still update children's IsSelected visual state(they are gonna have their checkboxes disabled)
            if (node.Children.Count > 0)
            {
                SetIsSelectedRecursively(node.Children, true);
            }
        }
        else
        {
            // Remove the parent node
            SelectedNodes.Remove(node);
            
            // When deselecting a parent, also deselect all children
            if (node.Children.Count > 0)
            {
                SetIsSelectedRecursively(node.Children, false);
            }
        }
        
        // Update IsEnabled state for all children since parent's selection changed
        UpdateChildrenEnabledState(node);
    }

    /// <summary>
    /// Recursively removes all descendants of a node from the SelectedNodes collection.
    /// Used when a parent is selected to prevent double-counting children.
    /// </summary>
    private void RemoveDescendantsFromSelectedNodes(TreeNode node)
    {
        foreach (var child in node.Children)
        {
            SelectedNodes.Remove(child);
            if (child.Children.Count > 0)
            {
                RemoveDescendantsFromSelectedNodes(child);
            }
        }
    }
    
    /// <summary>
    /// Recursively sets the IsSelected property on all nodes in the collection and their descendants.
    /// </summary>
    private void SetIsSelectedRecursively(ObservableCollection<TreeNode> nodes, bool isSelected)
    {
        _isUpdatingSelection = true;
        try
        {
            SetIsSelectedRecursivelyInternal(nodes, isSelected);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }
    
    private void SetIsSelectedRecursivelyInternal(ObservableCollection<TreeNode> nodes, bool isSelected)
    {
        foreach (var child in nodes)
        {
            
            // Only update if the value is different to avoid unnecessary property change notifications
            if (child.IsCheckboxSelected != isSelected)
            {
                child.IsCheckboxSelected = isSelected;
                
                // Only update the SelectedNodes collection when deselecting
                // When selecting, we don't add children since parent selection should be enough
                if (!isSelected)
                {
                    SelectedNodes.Remove(child);
                }
            }
            
            // Recursively update all already-loaded children
            // Note: Newly loaded children will inherit the IsSelected state from their parent
            // when they are loaded via LoadChildrenAsync in DirectoryScanService
            if (child.Children.Count > 0)
            {
                SetIsSelectedRecursivelyInternal(child.Children, isSelected);
            }
        }
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
    
    /// <summary>
    /// Recursively updates the IsEnabled state for all descendants.
    /// Children are disabled if they have any ancestor that is selected.
    /// </summary>
    private static void UpdateChildrenEnabledState(TreeNode node)
    {
        foreach (var child in node.Children)
        {
            child.IsCheckboxEnabled = !IsAnyAncestorSelected(child);
            // Recursively update grandchildren
            UpdateChildrenEnabledState(child);
        }
    }

    private static bool IsAnyAncestorSelected(TreeNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current.IsCheckboxSelected)
                return true;
            current = current.Parent;
        }
        return false;
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
        if (!_subscribedNodes.Add(node))
            return; // preventing another subscription for the same node, example after collapsing/expanding which can trigger multiple PropertyChanged events for the same node
        
        // Create and store the handler so we can unsubscribe later
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(TreeNode.IsCheckboxSelected))
            {
                OnTreeNodeSelectionChanged(node);
            }
        };
        
        _nodePropertyHandlers[node] = handler;
        node.PropertyChanged += handler;
    }
    
    private void UnsubscribeFromAllNodes()
    {
        foreach (var kvp in _nodePropertyHandlers)
        {
            kvp.Key.PropertyChanged -= kvp.Value;
        }
        _nodePropertyHandlers.Clear();
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
        
            // Unsubscribe from all node property changes
            UnsubscribeFromAllNodes();
        }
        _disposed = true;
    }
}