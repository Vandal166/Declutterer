using System;
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
    [NotifyPropertyChangedFor(nameof(IsTreeDataGridVisible))] // if NoChildrenFound changes, we also want to notify that IsTreeDataGridVisible has changed since it depends on NoChildrenFound
    private bool _noChildrenFound = false; // flag to indicate that no children were found for the selected directories based on the scan options
    
    [ObservableProperty]
    private string _selectedNodesSizeText = string.Empty; // a user-friendly string representation of the total size of the currently selected nodes
    
    [ObservableProperty]
    private bool isHistoryVisible = false; // flag to show/hide the history view

    [ObservableProperty]
    private HistoryWindowViewModel _historyViewModel; // ViewModel for the history view
    
    public bool IsExpandingAll { get; private set; } = false; // Flag to prevent multiple simultaneous expand/collapse operations

    public bool IsTreeDataGridVisible => Roots.Count > 0 && !NoChildrenFound; // The TreeDataGrid is visible if there are roots to display and we didn't just find that there are no children based on the scan options
    
    private readonly IContextMenuService _contextMenuService;
    private readonly ICommandService _commandService;
    private readonly INavigationService _navigationService;
    private readonly IScanWorkflowService _scanWorkflowService;
    private readonly ITreeNavigationService _treeNavigationService;
    private readonly IClipboardService _clipboardService;
    private readonly ISelectionManagementService _selectionManagementService;
    private readonly EventHandler<PropertyChangedEventArgs>? _selectionChangedHandler;

    private ScanOptions? _currentScanOptions;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    
    /// <summary>
    /// Event raised when the cleanup window closes. Allows the view to perform cleanup operations.
    /// </summary>
    public event EventHandler? CleanupWindowClosed;
    
    public MainWindowViewModel(INavigationService navigationService, IScanWorkflowService scanWorkflowService, ITreeNavigationService treeNavigationService,
        IContextMenuService contextMenuService, ICommandService commandService, IClipboardService clipboardService, ISelectionManagementService selectionManagementService, HistoryWindowViewModel historyViewModel)
    {
        _navigationService = navigationService;
        _scanWorkflowService = scanWorkflowService;
        _treeNavigationService = treeNavigationService;
        _contextMenuService = contextMenuService;
        _commandService = commandService;
        _clipboardService = clipboardService;
        _selectionManagementService = selectionManagementService;
        _historyViewModel = historyViewModel;

        // selection change tracking
        SelectedNodes.CollectionChanged += OnSelectedNodesCollectionChanged;
        
        // root collection tracking to notify about IsTreeDataGridVisible changes
        Roots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsTreeDataGridVisible));
        
        // sub to selection changes on all nodes to keep SelectedNodes collection in sync
        _selectionChangedHandler = (node, args) =>
        {
            if (node is TreeNode treeNode)
            {
                _selectionManagementService.HandleNodeSelectionChanged(treeNode, SelectedNodes);
            }
        };
        _selectionManagementService.OnNodePropertyChanged += _selectionChangedHandler;
    }

    public MainWindowViewModel() { } // for designer

    private void OnSelectedNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSelectedNodesSize();
        
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

            IsAnyNodeLoading = true;
            try
            {
                bool scanSucceeded = await _scanWorkflowService.ExecuteScanAsync(result, Roots);
                NoChildrenFound = !scanSucceeded; // if scan failed or all roots have no children then set NoChildrenFound to true
            }
            finally
            {
                IsAnyNodeLoading = false;
            }
        }
    }

    [RelayCommand]
    private void ShowHistoryInline()
    {
        // Create history view model with callback to hide history
        HistoryViewModel.SetHideHistoryCallback(HideHistoryInline);
        IsHistoryVisible = true;
    }

    [RelayCommand]
    private void HideHistoryInline()
    {
        // Hide the history view and return to main view
        IsHistoryVisible = false;
        //_historyViewModel = null;
    }
    
    [RelayCommand]
    private async Task ShowCleanupWindowAsync()
    {
        if (SelectedNodes.Count == 0)
            return;

        await _navigationService.ShowCleanupWindowAsync(SelectedNodes);
        
        // Signal that cleanup window has closed
        CleanupWindowClosed?.Invoke(this, EventArgs.Empty);
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
        
            // Unsubscribe from selection management service events
            if (_selectionChangedHandler != null)
            {
                _selectionManagementService.OnNodePropertyChanged -= _selectionChangedHandler;
            }
        
            // Dispose selection management service
            _selectionManagementService.Dispose();
        }
        _disposed = true;
    }
}