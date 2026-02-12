using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Declutterer.Models;
using Declutterer.ViewModels;
using Serilog;

namespace Declutterer.Services;

public sealed class TreeGridInteractionService
{
    private readonly MainWindowViewModel _mainWindowViewModel;
    public bool IsExpandingAll { get; private set; } = false; // Flag to prevent multiple simultaneous expand/collapse operations

    public TreeGridInteractionService(MainWindowViewModel mainWindowViewModel)
    {
        _mainWindowViewModel = mainWindowViewModel;
    }
    
    public void InitializeHandler(TreeDataGrid treeDataGrid)
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
            await _mainWindowViewModel.LoadChildrenParallelAsync(new List<TreeNode>() { node});
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