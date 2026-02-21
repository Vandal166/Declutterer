using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Utilities.Helpers;
using Serilog;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Services.Icons;

/// <summary>
/// Schedules and offloads icon loading tasks to background threads to keep the UI responsive.
/// </summary>
public class IconLoadingScheduler
{
    private readonly IIconLoader _iconLoaderService;
    private readonly ConcurrentDictionary<TreeNode, Task> _loadingTasks = new();
    private readonly ConcurrentHashSet<string> _loadedPaths = new();
    private CancellationTokenSource? _cancellationTokenSource;

    public IconLoadingScheduler(IIconLoader iconLoaderService)
    {
        _iconLoaderService = iconLoaderService;
    }

    /// <summary>
    /// Clears the cache of loaded icon paths. Call this before a new scan to ensure icons are reloaded.
    /// </summary>
    public void ClearLoadedPathsCache()
    {
        _loadedPaths.Clear();
        _iconLoaderService.ClearCache();
    }

    /// <summary>
    /// Loads an icon for a root node specifically.
    /// </summary>
    public async Task LoadIconForRootAsync(TreeNode rootNode)
    {
        var loadTask = LoadIconInternalAsync(rootNode);
        try
        {
            await loadTask;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load icon for root node '{Path}'", rootNode.FullPath);
        }
    }

    /// <summary>
    /// Requests lazy loading of icons for visible nodes.
    /// Icons are loaded asynchronously without blocking the UI.
    /// </summary>
    public void RequestIconLoadForVisibleNodes(IEnumerable<TreeNode> visibleNodes)
    {
        var nodesToLoad = visibleNodes
            .Where(node => node.Icon is null && !_loadingTasks.ContainsKey(node) && !_loadedPaths.Contains(node.FullPath))
            .ToList();

        if (nodesToLoad.Count == 0)
            return;

        // Fire and forget - load icons asynchronously without blocking
        foreach (var node in nodesToLoad)
        {
            _ = LoadIconForNodeAsync(node);
        }
    }

    /// <summary>
    /// Loads an icon for a single node asynchronously.
    /// </summary>
    private async Task LoadIconForNodeAsync(TreeNode node)
    {
        // Avoid duplicate loads
        if (!_loadingTasks.TryAdd(node, Task.CompletedTask))
            return;

        var loadTask = LoadIconInternalAsync(node);
        _loadingTasks[node] = loadTask;

        try
        {
            await loadTask;
        }
        finally
        {
            _loadingTasks.TryRemove(node, out _);
        }
    }

    private async Task LoadIconInternalAsync(TreeNode node)
    {
        try
        {
            var icon = await _iconLoaderService.LoadIconAsync(node.FullPath, node.IsDirectory);
            
            if (icon != null)
            {
                node.Icon = icon;
                _loadedPaths.Add(node.FullPath);
                Log.Information("Icon loaded for node: {NodeFullPath}", node.FullPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load icon for '{Path}'", node.FullPath);
        }
    }

    /// <summary>
    /// Cancels all pending icon loading operations.
    /// </summary>
    public void CancelAllLoading()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void Dispose()
    {
        CancelAllLoading();
        _cancellationTokenSource?.Dispose();
    }
}