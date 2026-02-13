using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Declutterer.Models;
using Declutterer.ViewModels;
using Serilog;

namespace Declutterer.Services;

public sealed class TreeGridInteractionService
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IconLoadingService _iconLoadingService;
    
    private bool IsExpandingAll { get; set; } = false; // Flag to prevent multiple simultaneous expand/collapse operations
    private double _lastPointerPressedTime = 0; // For detecting double-clicks on expanders

    //TODO: for some reason the Alt+Click loading will be trigered when COLLAPSING
    
    //TODO 2: analyze if there is an unsubcribtion needed
    public TreeGridInteractionService(MainWindowViewModel viewModel, IconLoadingService iconLoadingService)
    {
        _viewModel = viewModel;
        _iconLoadingService = iconLoadingService;
    }
    
    /// <summary>
    /// Initializes all necessary event handlers for the TreeDataGrid
    /// </summary>
    public void InitializeHandlers(TreeDataGrid treeDataGrid, HierarchicalTreeDataGridSource<TreeNode> source)
    {
        // sub to row expanding event to trigger lazy loading
        InitializeRowExpandingHandler(source);
        
        InitializeIconHandler();

        InitializeRowCollapsingHandler(source);
        
        // Handle pointer events to detect Alt+Click on expander
        InitializePointerPressedHandler(treeDataGrid);
                
        InitializePointerPressedEvent(treeDataGrid);
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
                    _ = _iconLoadingService.LoadIconForRootAsync(root);
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
                                    _ = HandleAltClickExpandAsync(node, willExpand);
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

    private void InitializePointerPressedHandler(TreeDataGrid treeDataGrid)
    {
        treeDataGrid.PointerPressed += async (sender, args) =>
        {
            if(IsExpandingAll)
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
    }

    private void InitializeRowCollapsingHandler(HierarchicalTreeDataGridSource<TreeNode> source)
    {
        source.RowCollapsing += (_, args) =>
        {
            if(IsExpandingAll)
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
            if(IsExpandingAll) 
                return; // Skip if we're already in the middle of an expand/collapse all operation triggered by Alt+Click
            
            if (args.Row.Model is { IsDirectory: true, HasChildren: true} node)
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
                
                // Subscribe to PropertyChanged for each child to detect IsSelected changes
                foreach (var child in node.Children)
                {
                    _viewModel.SubscribeToNodeSelectionChanges(child);
                }
                
                // Request lazy loading of icons for visible children
                _iconLoadingService.RequestIconLoadForVisibleNodes(node.Children);
            }
        };
    }

    private async Task HandleAltClickExpandAsync(TreeNode node, bool shouldExpand)
    {
        var sw = new Stopwatch();
        sw.Start();
        try
        {
            IsExpandingAll = true;
            // Pass isRoot=true so we skip setting IsExpanded on the clicked root node (the TreeDataGrid handles the root node's toggle via normal click processing)
            await ToggleAllDescendantsAsync(node, shouldExpand, isRoot: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Alt+Click expand/collapse for node '{NodeName}'", node.Name);
        }
        finally
        {
            IsExpandingAll = false;
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
    /// <param name="isRoot">True if this is the root node of the Alt+Click (its toggle is handled by TreeDataGrid)</param>
    private async Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, bool isRoot = false)
    {
        // Load children if not already loaded
        if (node.Children.Count == 0 && node is { IsDirectory: true, HasChildren: true })
        {
            await _viewModel.LoadChildrenParallelAsync(new List<TreeNode>() { node});
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
                .Select(child => ToggleAllDescendantsAsync(child, shouldExpand, isRoot: false))
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
}