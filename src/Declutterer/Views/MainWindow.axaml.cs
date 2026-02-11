using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Declutterer.ViewModels;
using Declutterer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Layout;
using Serilog;

namespace Declutterer.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingSelection = false; // Guard against re-entrancy during recursive selection updates
    private bool _isExpandingAll = false; // Flag to prevent multiple simultaneous expand/collapse operations
    private double _lastPointerPressedTime = 0; // For detecting double-clicks on expanders
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Set up the ViewModel with the TopLevel for folder picker
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(this);
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
   //TODO 3: add a context menu to the rows with options like "Open in Explorer", "Copy Path", "Delete",
        if (DataContext is MainWindowViewModel viewModel)
        {
            // finding TreeDataGrid control and setting up the hierarchical data source for it
            var treeDataGrid = this.FindControl<TreeDataGrid>("TreeDataGrid");
            if (treeDataGrid != null)
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
                                    IsEnabled = !IsAnyAncestorSelected(node) // disable checkbox if the parent is selected (except for root-most nodes(the ones with depth == 0))
                                };
                                
                                // Sync from CheckBox to TreeNode
                                checkBox.IsCheckedChanged += (s, e) =>
                                {
                                    if (checkBox.IsChecked.HasValue)
                                    {
                                        node.IsSelected = checkBox.IsChecked.Value;
                                        if(node.IsSelected)
                                            viewModel.SelectedNodes.Add(node);
                                        else
                                            viewModel.SelectedNodes.Remove(node);
                                    }
                                };
                                
                                // Sync from TreeNode to CheckBox
                                node.PropertyChanged += (s, e) =>
                                {
                                    if (e.PropertyName == nameof(TreeNode.IsSelected))
                                    {
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        {
                                            checkBox.IsChecked = node.IsSelected;
                                            checkBox.IsEnabled = !IsAnyAncestorSelected(node);
                                            checkBox.InvalidateVisual();
                                            
                                        }, Avalonia.Threading.DispatcherPriority.Render);
                                    }
                                    // this makes sure that if all children node's IsSelected is true, then we update the enabled state of the children the next time the parent IsSelected is true,
                                    // example: we select all children then select the parent -> set IsEnable to false for children
                                    var current = node.Parent;
                                    while (current != null)
                                    {
                                        current.PropertyChanged += (_, e) =>
                                        {
                                            if (e.PropertyName == nameof(TreeNode.IsSelected))
                                            {
                                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                                {
                                                    checkBox.IsEnabled = !IsAnyAncestorSelected(node);
                                                    checkBox.InvalidateVisual();
                                                }, Avalonia.Threading.DispatcherPriority.Render);
                                            }
                                        };
                                        current = current.Parent;
                                    }
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
                            MaxWidth = new GridLength((GetTopLevel(this)?.Bounds.Width ?? 900) / 3), // this will make the path column take up at most 1/3 of the window width
                        }),
                    }
                };
                
                // sub to row expanding event to trigger lazy loading
                source.RowExpanding += async (sender, args) =>
                {
                    if(_isExpandingAll) 
                        return; // Skip if we're already in the middle of an expand/collapse all operation triggered by Alt+Click
                    
                    if (args.Row.Model is { IsDirectory: true, HasChildren: true} node)
                    {
                        // if children not loaded yet then load them and skip loading children for root nodes since we already load them with children in the initial scan
                        // NOTE: this fixed the duplicate entries issue
                        if (node.Children.Count == 0 && node.Depth != 0) 
                        {
                            await viewModel.LoadChildrenForNodeAsync(node);
                        }
                       
                        // Pre-load grandchildren for any child directories that don't have their children loaded yet
                        foreach (var child in node.Children.Where(c => c is { IsDirectory: true, HasChildren: true, Children.Count: 0 }))
                        {
                            _ = viewModel.LoadChildrenForNodeAsync(child);
                        }
                        
                        // Subscribe to PropertyChanged for each child to detect IsSelected changes
                        foreach (var child in node.Children)
                        {
                            SubscribeToNodeSelectionChanges(child);
                        }
                    }
                };
                
                // Subscribe to PropertyChanged for root nodes
                foreach (var root in viewModel.Roots)
                {
                    SubscribeToNodeSelectionChanges(root);
                }
                
                source.RowCollapsing += (sender, args) =>
                {
                    if(_isExpandingAll)
                        return;
                    
                    if (args.Row.Model is TreeNode node) // setting IsExpanded to false on collapse for the node
                    {
                        node.IsExpanded = false;
                    }
                };
                
                // Handle pointer events to detect Alt+Click on expander
                treeDataGrid.PointerPressed += async (sender, args) =>
                {
                    if(_isExpandingAll)
                        return;
                    
                    if(args.GetCurrentPoint(treeDataGrid).Properties.IsLeftButtonPressed)
                    {
                        double currentTime = args.Timestamp;
                        if (currentTime - _lastPointerPressedTime < 300) // 300ms threshold for double-click
                        {
                            //TODO let the vm handle and open in explroer on double click
                            return;
                        }
                        _lastPointerPressedTime = currentTime;
                    }
                };
                
                treeDataGrid.AddHandler(InputElement.PointerPressedEvent, (sender, args) =>
                {
                    if ((args.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt && args.GetCurrentPoint(treeDataGrid).Properties.IsLeftButtonPressed)
                    {
                        var point = args.GetCurrentPoint(treeDataGrid);
                        var visual = treeDataGrid.InputHitTest(point.Position) as Control;

                        while (visual != null)
                        {
                            if (visual.Name == "PART_ExpanderButton" || visual.GetType().Name.Contains("ToggleButton"))
                            {
                                var parent = visual.Parent as Control;
                                while (parent != null)
                                {
                                    if (parent is TreeDataGridRow row && row.DataContext is TreeNode node)
                                    {
                                        if (node.HasChildren)
                                        {
                                            bool willExpand = !node.IsExpanded;
                                            
                                            // Fire and forget - expand/collapse all descendants
                                            _ = HandleAltClickExpandAsync(node, viewModel, willExpand);
                                        }
                                        break;
                                    }
                                    parent = parent.Parent as Control;
                                }
                                break;
                            }
                            visual = visual.Parent as Control;
                        }
                    }
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

                
                treeDataGrid.Source = source; // assigning the source to the TreeDataGrid so that it can display the data
            }
        }
    }
    
    private void SubscribeToNodeSelectionChanges(TreeNode node)
    {
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

        // Only propagate to currently-loaded children
        // Newly-loaded children will inherit the IsSelected state from their parent via DirectoryScanService
        if (node.Children.Count == 0)
            return; // No children to update
        
        // Recursively set IsSelected on all currently-loaded descendants
        SetIsSelectedRecursively(node.Children, node.IsSelected);
    }
    
    /// <summary>
    /// Recursively sets the IsSelected property on all nodes in the collection and their descendants.
    /// </summary>
    private void SetIsSelectedRecursively(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes, bool isSelected)
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
    
    private void SetIsSelectedRecursivelyInternal(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes, bool isSelected)
    {
        foreach (var child in nodes)
        {
            // Only update if the value is different to avoid unnecessary property change notifications
            if (child.IsSelected != isSelected)
            {
                child.IsSelected = isSelected;
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
    
    private async Task HandleAltClickExpandAsync(TreeNode node, MainWindowViewModel viewModel, bool shouldExpand)
    {
        var sw = new Stopwatch();
        sw.Start();
        try
        {
            _isExpandingAll = true;
            // Pass isRoot=true so we skip setting IsExpanded on the clicked root node (the TreeDataGrid handles the root node's toggle via normal click processing)
            await ToggleAllDescendantsAsync(node, shouldExpand, viewModel, isRoot: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Alt+Click expand/collapse for node '{NodeName}'", node.Name);
        }
        finally
        {
            _isExpandingAll = false;
            sw.Stop();
            Log.Information("Completed Alt+Click expand/collapse for node '{NodeName}' in {ElapsedMilliseconds} ms", node.Name, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Recursively toggles the expansion state of a node and all its descendants.
    /// Uses parallel processing with task batching to efficiently handle large directory trees.
    /// </summary>
    /// <param name="node">The node to process</param>
    /// <param name="shouldExpand">Whether to expand or collapse</param>
    /// <param name="viewModel">The view model for loading children</param>
    /// <param name="isRoot">True if this is the root node of the Alt+Click (its toggle is handled by TreeDataGrid)</param>
    private async Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, MainWindowViewModel viewModel, bool isRoot = false)
    {
        // Load children if not already loaded
        if (node.Children.Count == 0 && node is { IsDirectory: true, HasChildren: true })
        {
            await viewModel.LoadChildrenParallelAsync(new List<TreeNode>() { node});
        }
        
        foreach (var child in node.Children)
        {
            child.IsExpanded = shouldExpand;
        }
        
        // Process all child directories recursively in parallel
        var directoryChildren = node.Children
            .Where(child => child is { IsDirectory: true, HasChildren: true })
            .ToList();
        
        if (directoryChildren.Count > 0)
        {
            // Process all directory children concurrently
            var tasks = directoryChildren
                .Select(child => ToggleAllDescendantsAsync(child, shouldExpand, viewModel, isRoot: false))
                .ToList();
            
            await Task.WhenAll(tasks);
        }
        
        // Only set expansion state for this node if it's NOT the root of the Alt+Click
        // The TreeDataGrid handles the root node's toggle via normal click processing
        if (!isRoot)
        {
            node.IsExpanded = shouldExpand;
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