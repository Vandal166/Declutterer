using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Declutterer.UI.Services.Icons;
using MainWindowViewModel = Declutterer.UI.ViewModels.MainWindowViewModel;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Services.Interaction;

public sealed class TreeGridInteractionService
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IconLoadingScheduler _iconLoadingScheduler;
    private double _lastPointerPressedTime = 0; // For detecting double-clicks
    
    public TreeGridInteractionService(MainWindowViewModel viewModel, IconLoadingScheduler iconLoadingScheduler)
    {
        _viewModel = viewModel;
        _iconLoadingScheduler = iconLoadingScheduler;
    }
    
    /// <summary>
    /// Initializes all necessary event handlers for an TreeDataGrid. Invoked once during MainWindow initialization
    /// </summary>
    public void InitializeHandlers(TreeDataGrid treeDataGrid, HierarchicalTreeDataGridSource<TreeNode> source)
    {
        // sub to row expanding event to trigger lazy loading
        InitializeRowExpandingHandler(source);
        
        InitializeIconHandler();

        InitializeRowCollapsingHandler(source);
                
        // Handle pointer events to detect Alt+Click on expander
        InitializePointerPressedEvent(treeDataGrid);
    }
    
    /// <summary>
    /// Initializes a pointer double-pressed handler on the given control. When a double-click is detected, it resolves the TreeNode under the pointer and invokes the provided callback.
    /// </summary>
    /// <param name="control">The control to attach the handler to</param>
    /// <param name="onNodeDoubleClick">Callback to invoke with the TreeNode that was double-clicked (or null if no node)</param>
    /// <param name="onBeforeActionCondition">Optional callback to check a condition before processing the double-click. If it evaluates to true the double-click action will be skipped.</param>
    public void InitializePointerDoublePressedHandler(Control control, Action<TreeNode?> onNodeDoubleClick, Func<bool>? onBeforeActionCondition = null)
    {
        control.PointerPressed += (_, args) =>
        {
            if(onBeforeActionCondition != null && onBeforeActionCondition())
                return;
            
            if(args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                double currentTime = args.Timestamp;
                if (currentTime - _lastPointerPressedTime < 300) // 300ms threshold for double-click
                {
                    onNodeDoubleClick(GetNodeFromPointerEvent(control, args));
                    return;
                }
                _lastPointerPressedTime = currentTime;
            }
        };
    }

    private void InitializeIconHandler()
    {
        _viewModel.Roots.CollectionChanged += (sender, args) =>
        {
            // When the Roots collection changes (e.g. new scan), subscribe to new root nodes and load their icons
            foreach (var root in _viewModel.Roots)
            {
                _viewModel.SubscribeToNodeSelectionChanges(root);
    
                // Load icon for root node
                if (root.Icon is null)
                {
                    _ = _iconLoadingScheduler.LoadIconForRootAsync(root);
                }
            }
        };
    }

    private void InitializePointerPressedEvent(TreeDataGrid treeDataGrid)
    {
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
                                    _ = _viewModel.HandleAltClickExpandAsync(node, willExpand);
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
    }

    private void InitializeRowCollapsingHandler(HierarchicalTreeDataGridSource<TreeNode> source)
    {
        source.RowCollapsing += (_, args) =>
        {
            if( _viewModel.IsExpandingAll)
                return;
            
            if (args.Row.Model is TreeNode node) // setting IsExpanded to false on collapse for the node
            {
                node.IsExpanded = false;
            }
        };
    }

    private void InitializeRowExpandingHandler(HierarchicalTreeDataGridSource<TreeNode> source)
    {
        source.RowExpanding += async (sender, args) =>
        {
            if (args.Row.Model is { IsDirectory: true, HasChildren: true} node)
            {
                if (!_viewModel.IsExpandingAll)
                {
                    // if children not loaded yet then load them and skip loading children for root nodes since we already load them with children in the initial scan
                    // NOTE: this fixed the duplicate entries issue
                    if (node.Children.Count == 0 && node.Depth != 0) 
                    {
                        await _viewModel.LoadChildrenForNodeAsync(node);
                    }
                   
                    // Pre-load grandchildren for any child directories that don't have their children loaded yet
                    foreach (var child in node.Children.Where(c => c is { IsDirectory: true, HasChildren: true, Children.Count: 0 }))
                    {
                        _ = _viewModel.LoadChildrenForNodeAsync(child);
                    }
                }
                
                // Subscribe to PropertyChanged for each child to detect IsSelected changes
                foreach (var child in node.Children)
                {
                    _viewModel.SubscribeToNodeSelectionChanges(child);
                }
                
                // ALWAYS request lazy loading of icons for visible children, even during Alt+Click expansion
                _iconLoadingScheduler.RequestIconLoadForVisibleNodes(node.Children);
            }
        };
    }

    private static TreeNode? GetNodeFromPointer(Control Control, Avalonia.Point point)
    {
        var visual = Control.InputHitTest(point) as Control;
        while (visual != null)
        {
            if (visual.DataContext is TreeNode node)
                return node;
            visual = visual.Parent as Control;
        }
        return null;
    }
    private static TreeNode? GetNodeFromPointerEvent(Control control, PointerPressedEventArgs args)
    {
        var point = args.GetCurrentPoint(control).Position;
        return GetNodeFromPointer(control, point);
    }
}