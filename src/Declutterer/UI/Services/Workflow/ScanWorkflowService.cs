using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Scanning;
using Declutterer.UI.Services.Icons;

namespace Declutterer.UI.Services.Workflow;

public sealed class ScanWorkflowService : IScanWorkflowService
{
    private readonly DirectoryScanService _directoryScanService;
    private readonly IconLoadingScheduler _iconLoadingScheduler;
    private readonly IDispatcher _dispatcher;

    public ScanWorkflowService(DirectoryScanService directoryScanService, IconLoadingScheduler iconLoadingScheduler, IDispatcher dispatcher)
    {
        _directoryScanService = directoryScanService;
        _iconLoadingScheduler = iconLoadingScheduler;
        _dispatcher = dispatcher;
    }

    /// <returns>true if at least one valid root was added and scanned, false if no valid roots were found or all roots had no children</returns>
    public async Task<bool> ExecuteScanAsync(ScanOptions scanOptions, ObservableCollection<TreeNode> roots)
    {
        _iconLoadingScheduler.ClearLoadedPathsCache();

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