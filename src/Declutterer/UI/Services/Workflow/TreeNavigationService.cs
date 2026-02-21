using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Services.Scanning;
using Serilog;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Services.Workflow;

public sealed class TreeNavigationService : ITreeNavigationService
{
    private readonly DirectoryScanService _directoryScanService;
    private readonly IDispatcher _dispatcher;
    private readonly IScanWorkflowService _scanWorkflowService;
    
    // Batch size for expansion operations to balance UI responsiveness with performance.
    // This value limits concurrent operations that schedule work on the UI thread,
    // preventing the dispatcher queue from being overwhelmed during Alt+Click expansion.
    private const int ExpansionBatchSize = 20;

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
        // Process in batches to avoid blocking the UI thread when there are many children
        for (int i = 0; i < node.Children.Count; i += ExpansionBatchSize)
        {
            int batchEnd = Math.Min(i + ExpansionBatchSize, node.Children.Count);
            await _dispatcher.InvokeAsync(() =>
            {
                for (int j = i; j < batchEnd; j++)
                {
                    node.Children[j].IsExpanded = shouldExpand;
                }
            });
            
            // Yield control to allow the dispatcher to process queued messages and update the UI
            await _dispatcher.InvokeAsync(() => { });
        }
        
        // Process all child directories recursively in parallel
        var directoryChildren = node.Children
            .Where(child => child is { IsDirectory: true, HasChildren: true })
            .ToList();
        
        if (directoryChildren.Count > 0)
        {
            // Process directory children in smaller batches to allow UI updates
            // This prevents the UI thread from being overwhelmed with property changes
            for (int batchStartIndex = 0; batchStartIndex < directoryChildren.Count; batchStartIndex += ExpansionBatchSize)
            {
                int batchItemCount = Math.Min(ExpansionBatchSize, directoryChildren.Count - batchStartIndex);
                var tasks = new List<Task>(batchItemCount);
                
                for (int i = 0; i < batchItemCount; i++)
                {
                    var child = directoryChildren[batchStartIndex + i];
                    tasks.Add(ToggleAllDescendantsAsync(child, shouldExpand, isRoot: false, currentScanOptions));
                }
                
                await Task.WhenAll(tasks);
                
                // Yield control to allow the dispatcher to process queued messages and update the UI
                await _dispatcher.InvokeAsync(() => { });
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


