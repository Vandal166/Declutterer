using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Models;
using Serilog;

namespace Declutterer.Services;

public sealed class TreeNavigationService : ITreeNavigationService
{
    private readonly DirectoryScanService _directoryScanService;
    private readonly IDispatcher _dispatcher;
    private readonly IScanWorkflowService _scanWorkflowService;

    public TreeNavigationService(DirectoryScanService directoryScanService, IDispatcher dispatcher, IScanWorkflowService scanWorkflowService)
    {
        _directoryScanService = directoryScanService ?? throw new ArgumentNullException(nameof(directoryScanService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scanWorkflowService = scanWorkflowService ?? throw new ArgumentNullException(nameof(scanWorkflowService));
    }

    public async Task LoadChildrenForNodeAsync(TreeNode node, ScanOptions? currentScanOptions)
    {
        if (node.Children.Count > 0)
            return; // Already loaded

        try
        {
            var children = await _directoryScanService.LoadChildrenAsync(node, currentScanOptions);
            
            await _dispatcher.InvokeAsync(() =>
            {
                foreach (var child in children)
                {
                    node.Children.Add(child);
                }
            });
            
            // Pre-load children for subdirectories (one level ahead) so expansion works immediately
            var preloadTasks = children
                .Where(c => c is { IsDirectory: true, HasChildren: true })
                .Select(child => PreloadChildrenAsync(child, currentScanOptions));
            await Task.WhenAll(preloadTasks);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load children for node: {NodePath}", node.FullPath);
            throw;
        }
    }
    public async Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, bool isRoot = false, ScanOptions? currentScanOptions = null)
    {
        // Load children if not already loaded
        if (node.Children.Count == 0 && node is { IsDirectory: true, HasChildren: true } && shouldExpand)
        {
            try
            {
                await _scanWorkflowService.LoadChildrenParallelAsync(new List<TreeNode> { node }, currentScanOptions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load children during toggle for node: {NodePath}", node.FullPath);
                throw;
            }
        }
        
        // Set expansion state for all children on the UI thread
        // This ensures the TreeDataGrid receives property change notifications
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var child in node.Children)
            {
                child.IsExpanded = shouldExpand;
            }
        });
        
        // Process all child directories recursively in parallel
        var directoryChildren = node.Children
            .Where(child => child is { IsDirectory: true, HasChildren: true })
            .ToList();
        

        if (directoryChildren.Count > 0)
        {
            // Process directory children in smaller batches to allow UI updates
            // This prevents the UI thread from being overwhelmed with property changes
            const int batchSize = 20;
            for (int i = 0; i < directoryChildren.Count; i += batchSize)
            {
                var batch = directoryChildren.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(child => ToggleAllDescendantsAsync(child, shouldExpand, isRoot: false, currentScanOptions));
                await Task.WhenAll(tasks);
                
                // Yield to UI thread after each batch to allow rendering
                await Task.Yield();
            }
        }
        
        // Only set expansion state for this node if it's NOT the root of the Alt+Click
        // The TreeDataGrid handles the root node's toggle via normal click processing
        if (!isRoot)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                node.IsExpanded = shouldExpand;
            });
        }
    }

    private async Task PreloadChildrenAsync(TreeNode node, ScanOptions? currentScanOptions)
    {
        if (node.Children.Count > 0)
            return; // Already loaded

        
        var children = await _directoryScanService.LoadChildrenAsync(node, currentScanOptions);
        
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var child in children)
            {
                node.Children.Add(child);
            }
        });
    }
}


