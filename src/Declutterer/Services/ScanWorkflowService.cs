using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Models;

namespace Declutterer.Services;

public sealed class ScanWorkflowService : IScanWorkflowService
{
    private readonly DirectoryScanService _directoryScanService;
    private readonly IconLoadingService _iconLoadingService;
    private readonly IDispatcher _dispatcher;

    public ScanWorkflowService(DirectoryScanService directoryScanService, IconLoadingService iconLoadingService, IDispatcher dispatcher)
    {
        _directoryScanService = directoryScanService;
        _iconLoadingService = iconLoadingService;
        _dispatcher = dispatcher;
    }

    public async Task<bool> ExecuteScanAsync(ScanOptions scanOptions, List<TreeNode> roots)
    {
        // Clear caches
        _iconLoadingService.ClearLoadedPathsCache();

        // Clear and prepare roots collection
        roots.Clear();

        var validRoots = new List<TreeNode>();
        foreach (var directoryPath in scanOptions.DirectoriesToScan.Where(Directory.Exists))
        {
            try
            {
                var rootNode = DirectoryScanService.CreateRootNode(directoryPath);
                roots.Add(rootNode);
                validRoots.Add(rootNode);
            }
            catch (UnauthorizedAccessException) { /*skip*/ }
        }

        if (validRoots.Count == 0)
            return false;

        // Load children for all roots
        return await LoadChildrenParallelAsync(validRoots, scanOptions);
    }

    public async Task<bool> LoadChildrenParallelAsync(List<TreeNode> validRoots, ScanOptions? scanOptions)
    {
        // Load children for all roots in parallel - returns a dictionary mapping each root to its children
        var childrenByRoot = await _directoryScanService.LoadChildrenForMultipleRootsAsync(validRoots, scanOptions);

        if (childrenByRoot.Values.All(children => children.Count == 0))
            return false; // No children found

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

        return true;
    }
}