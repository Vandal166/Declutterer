using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;
using Declutterer.Services;
using Declutterer.Views;

namespace Declutterer.ViewModels;

//TODO: add exclusions for Directories so they won't be scanned at all, not even shown in the tree, Persist the exclusions in some form of settings like json file

public partial class MainWindowViewModel : ViewModelBase
{
    // ObservableProperty is used to generate the property with INotifyPropertyChanged implementation which will notify the UI when the property changes
    [ObservableProperty]
    private bool _isAnyNodeLoading = false; // used for showing loading indicator on the UI
    
    [ObservableProperty]
    private bool _noChildrenFound = false; // flag to indicate that no children were found for the selected directories based on the scan options
    
    [ObservableProperty]
    private string _selectedNodesSizeText = string.Empty; // a user-friendly string representation of the total size of the currently selected nodes
    
    private readonly DirectoryScanService _directoryScanService;
    private readonly SmartSelectionService _smartSelectionService;
    
    private readonly IDispatcher _dispatcher;
    private readonly IconLoadingService _iconLoadingService;
    
    private bool _isUpdatingSelection = false; // Guard against re-entrancy during recursive selection updates

    private ScanOptions? _currentScanOptions;
    private TopLevel? _topLevel;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    private readonly HashSet<TreeNode> _subscribedNodes = new();
    
    public MainWindowViewModel(DirectoryScanService directoryScanService, SmartSelectionService smartSelectionService, IDispatcher dispatcher, IconLoadingService iconLoadingService)
    {
        _directoryScanService = directoryScanService;
        _smartSelectionService = smartSelectionService;
        _dispatcher = dispatcher;
        _iconLoadingService = iconLoadingService;

        // selection change tracking
        SelectedNodes.CollectionChanged += (s, e) =>
        {
            UpdateSelectedNodesSize();
        };
    }

    public MainWindowViewModel() { } // for designer
    
    private void UpdateSelectedNodesSize()
    {
        var selectedNodesSize = SelectedNodes.Sum(n => n.Size);
        SelectedNodesSizeText = ByteConverter.ToReadableString(selectedNodesSize);
    }

    public async Task LoadChildrenForNodeAsync(TreeNode node)
    {
        if (node.Children.Count > 0)
            return; // Already loaded
        
        IsAnyNodeLoading = true;
        try
        {
            var children = await _directoryScanService.LoadChildrenAsync(node, _currentScanOptions);
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var child in children)
                {
                    node.Children.Add(child);
                }
            });
            
            // Pre-load children for subdirectories (one level ahead) so expansion works immediately
            var preloadTasks = children
                .Where(c => c is { IsDirectory: true, HasChildren: true })
                .Select(PreloadChildrenAsync);
            await Task.WhenAll(preloadTasks);
        }
        finally
        {
            IsAnyNodeLoading = false;
        }
    }

    private async Task PreloadChildrenAsync(TreeNode node)
    {
        if (node.Children.Count > 0)
            return; // Already loaded

        var children = await _directoryScanService.LoadChildrenAsync(node, _currentScanOptions);
        
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var child in children)
            {
                node.Children.Add(child);
            }
        });
    }

    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    [RelayCommand]
    private void ClearAll()
    {
        Roots.Clear();
        SelectedNodes.Clear();
        _currentScanOptions = null;
        NoChildrenFound = false;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var node in SelectedNodes.ToList())
        {
            node.IsCheckboxSelected = false; // This will trigger the UI to uncheck the node and also update the SelectedNodes collection through the binding
        }
        SelectedNodes.Clear();
    }
    
    [RelayCommand]
    private void SmartSelect()
    {
        if (_currentScanOptions is null)
            return;
        
        DeselectAll();
        foreach (var rootChild in Roots)
        {
            var toSelect = _smartSelectionService.Select(rootChild, _currentScanOptions, new ScorerOptions());
            foreach (var selectedNode in toSelect)
            {
                selectedNode.IsCheckboxSelected = true; // Update the node's selection state for UI binding
                SelectedNodes.Add(selectedNode);
            }
        }
    }
    
    [RelayCommand]
    private async Task ShowScanOptionsWindowAsync()
    {
        var scanOptionsWindow = new ScanOptionsWindow
        {
            DataContext = new ScanOptionsWindowViewModel()
        };

        if (_topLevel is Window window)
        {
            // This triggers the scanning:
            
            var result = await scanOptionsWindow.ShowDialog<ScanOptions?>(window);
            if (result != null) // after we click on 'Scan' in the ScanOptionsWindow, we get the ScanOptions result here and proceed to scan
            {
                _currentScanOptions = result;

                // Clear all cached icons so icons are reloaded on re-scans
                _iconLoadingService.ClearLoadedPathsCache();
                IconLoaderService.ClearCache();

                Roots.Clear();
                NoChildrenFound = false;
                _subscribedNodes.Clear(); // Clear subscriptions for old roots

                var validRoots = new List<TreeNode>();
                foreach (var directoryPath in _currentScanOptions.DirectoriesToScan.Where(Directory.Exists))
                {
                    try
                    {
                        var rootNode = DirectoryScanService.CreateRootNode(directoryPath);
                        Roots.Add(rootNode);
                        validRoots.Add(rootNode);
                    }
                    catch (UnauthorizedAccessException){/*skip*/}
                }


                await LoadChildrenParallelAsync(validRoots);
            }
        }
    }

    public async Task LoadChildrenParallelAsync(List<TreeNode> validRoots)
    {
        IsAnyNodeLoading = true;
                
        try
        {
            // Load children for all roots in parallel - returns a dictionary mapping each root to its children
            var childrenByRoot = await _directoryScanService.LoadChildrenForMultipleRootsAsync(validRoots, _currentScanOptions);

            if (childrenByRoot.Values.All(children => children.Count == 0))
            {
                // No children were found for the selected directories based on the scan options
                NoChildrenFound = true;
                return;
            }
                    
            // Reset the flag since we found children
            NoChildrenFound = false;

            // Batch UI updates to reduce dispatcher overhead
            const int batchSize = 100;
            await _dispatcher.InvokeAsync(() =>
            {
                foreach (var root in validRoots)
                {
                    if (childrenByRoot.TryGetValue(root, out var children)) // if we got children for this root then add them to the root's Children collection
                    {
                        // Add children in batches to avoid overwhelming the UI thread
                        for (int i = 0; i < children.Count; i += batchSize)
                        {
                            var batch = children.Skip(i).Take(batchSize);
                            foreach (var child in batch)
                            {
                                root.Children.Add(child);
                            }
                        }
                        root.IsExpanded = true;
                    }
                }
            });
        }
        finally
        {
            IsAnyNodeLoading = false;
        }
    }

    [RelayCommand]
    private async Task ShowCleanupWindowAsync()
    {
        if (SelectedNodes.Count == 0)
            return;

        var cleanupWindow = new CleanupWindow
        {
            DataContext = new CleanupWindowViewModel(SelectedNodes.ToList()) // passing the selected nodes to the CleanupWindowViewModel so it can display them and perform cleanup actions
        };
        
        if (_topLevel is Window window)
        {
            await cleanupWindow.ShowDialog(window);
        }
    }
    public void SubscribeToNodeSelectionChanges(TreeNode node)
    {
        if (!_subscribedNodes.Add(node))
            return; // preventing another subscription for the same node, example after collapsing/expanding which can trigger multiple PropertyChanged events for the same node
        
        node.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TreeNode.IsCheckboxSelected))
            {
                OnTreeNodeSelectionChanged(node);
            }
        };
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
}