using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Common;
using Declutterer.Models;
using Declutterer.Services;
using Declutterer.Views;

namespace Declutterer.ViewModels;

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
    private readonly IIconLoader _iconLoaderService;
    
    private bool _isUpdatingSelection = false; // Guard against re-entrancy during recursive selection updates

    private ScanOptions? _currentScanOptions;
    private TopLevel? _topLevel;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    private readonly HashSet<TreeNode> _subscribedNodes = new();
    
    public MainWindowViewModel(DirectoryScanService directoryScanService, IIconLoader iconLoaderService, SmartSelectionService smartSelectionService)
    {
        _directoryScanService = directoryScanService;
        _iconLoaderService = iconLoaderService;
        _smartSelectionService = smartSelectionService;

        // Wire up selection change tracking
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
            node.IsSelected = false; // This will trigger the UI to uncheck the node and also update the SelectedNodes collection through the binding
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
                selectedNode.IsSelected = true; // Update the node's selection state for UI binding
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

                Roots.Clear();
                NoChildrenFound = false;

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
            
            // Batch UI updates to reduce dispatcher overhead - add children in chunks
            const int batchSize = 100;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
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

        foreach (var node in SelectedNodes)
        {
            var icon = await _iconLoaderService.LoadIconAsync(node.FullPath, node.IsDirectory);
            node.Icon = icon; // Update the node's icon property with the loaded icon, this
        }
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
            if (args.PropertyName == nameof(TreeNode.IsSelected))
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
        if (node.IsSelected)
        {
            SelectedNodes.Add(node);
            
            // removing all descendants from SelectedNodes since parent selection encompasses them
            RemoveDescendantsFromSelectedNodes(node);
            
            // But still update children's IsSelected visual state
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
            if (child.IsSelected != isSelected)
            {
                child.IsSelected = isSelected;
                
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
    private void UpdateNodeSelection((TreeNode, bool) nodeAndSelection)
    {
        var (node, isSelected) = nodeAndSelection;
        if (node.IsSelected == isSelected)
            return; // No change, skip

        node.IsSelected = isSelected;

        if (isSelected)
        {
            SelectedNodes.Add(node);
        }
        else
        {
            SelectedNodes.Remove(node);
        }
    }
    
    public HierarchicalTreeDataGridSource<TreeNode> CreateTreeDataGridSource(MainWindowViewModel viewModel, double? viewWidth = null)
    {
        var source = new HierarchicalTreeDataGridSource<TreeNode>(viewModel.Roots)
        {
            Columns =
            {
                new TemplateColumn<TreeNode>("Select", 
                    new FuncDataTemplate<TreeNode>((node, _) =>
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                        if(node is null)
                            return null;
                        //TODO make parent distinct from child nodes visually
                                
                        // Don't create checkbox for root nodes (Depth == 0)
                        if (node.Depth == 0)
                        {
                            node.IsExpanded = true; // auto expanded root node
                            return new Control();
                        }
                                
                        var checkBox = new CheckBox
                        {
                            IsChecked = node.IsSelected,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        };
                        
                        // Bind IsChecked to TreeNode.IsSelected
                        checkBox.Bind(ToggleButton.IsCheckedProperty, new Avalonia.Data.Binding(nameof(TreeNode.IsSelected))
                        {
                            Source = node,
                            Mode = Avalonia.Data.BindingMode.TwoWay
                        });
                        
                        // Bind IsEnabled to TreeNode.IsEnabled
                        checkBox.Bind(InputElement.IsEnabledProperty, new Avalonia.Data.Binding(nameof(TreeNode.IsEnabled))
                        {
                            Source = node,
                            Mode = Avalonia.Data.BindingMode.OneWay
                        });
                        
                        // Sync from CheckBox to TreeNode
                        checkBox.IsCheckedChanged += (s, e) =>
                        {
                            viewModel.UpdateNodeSelection((node, checkBox.IsChecked.Value));
                        };
                                
                        return checkBox;
                    }, supportsRecycling: false), // Disable recycling to ensure each node has its own CheckBox
                    options: new TemplateColumnOptions<TreeNode>
                    {
                        CanUserResizeColumn = false,
                        CanUserSortColumn = false,
                    }),
                new HierarchicalExpanderColumn<TreeNode>(
                    new TextColumn<TreeNode, string>("Name", x => x.Name, options: new TextColumnOptions<TreeNode>
                    {
                        TextTrimming = TextTrimming.PrefixCharacterEllipsis,
                        CanUserResizeColumn = true,
                        MaxWidth = new GridLength(400),
                        CanUserSortColumn = true,
                        CompareAscending = (a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase),
                        CompareDescending = (a, b) => string.Compare(b?.Name, a?.Name, StringComparison.OrdinalIgnoreCase),
                    }),
                    x => x.Children,
                    x => x.HasChildren,
                    x => x.IsExpanded),
                new TextColumn<TreeNode, string>("Size", x => x.SizeFormatted,
                    options: new TextColumnOptions<TreeNode>
                    {
                        TextAlignment = TextAlignment.Right,
                        CanUserResizeColumn = true,
                        CanUserSortColumn = true,
                        CompareAscending = (a, b) => a?.Size.CompareTo(b?.Size ?? 0) ?? 0,
                        CompareDescending = (a, b) => b?.Size.CompareTo(a?.Size ?? 0) ?? 0,
                    }),

                new TextColumn<TreeNode, DateTime?>("Last Modified", x => x.LastModified,
                    options: new TextColumnOptions<TreeNode>
                    {
                        TextAlignment = TextAlignment.Right,
                        CanUserResizeColumn = true,
                        CanUserSortColumn = true,
                        CompareAscending = (a, b) => Nullable.Compare(a?.LastModified, b?.LastModified),
                        CompareDescending = (a, b) => Nullable.Compare(b?.LastModified, a?.LastModified),
                    }),
                new TextColumn<TreeNode, string>("Path", x => x.FullPath, options: new TextColumnOptions<TreeNode>
                {
                    TextTrimming = TextTrimming.PathSegmentEllipsis,
                    CanUserSortColumn = false,
                    MaxWidth = new GridLength((viewWidth ?? 900) / 3), // this will make the path column take up at most 1/3 of the window width
                }),
            }
        };
        return source;
    }
    
    //TODO do the plan-mvvmRefactoring.prompt.md
    
    /// <summary>
    /// Recursively updates the IsEnabled state for all descendants.
    /// Children are disabled if they have any ancestor that is selected.
    /// </summary>
    private static void UpdateChildrenEnabledState(TreeNode node)
    {
        foreach (var child in node.Children)
        {
            child.IsEnabled = !IsAnyAncestorSelected(child);
            // Recursively update grandchildren
            UpdateChildrenEnabledState(child);
        }
    }

    private static bool IsAnyAncestorSelected(TreeNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current.IsSelected)
                return true;
            current = current.Parent;
        }
        return false;
    }
}