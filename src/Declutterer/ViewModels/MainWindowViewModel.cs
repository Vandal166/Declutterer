using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Common;
using Declutterer.Models;
using Declutterer.Services;
using Declutterer.Views;
using Serilog;

namespace Declutterer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // ObservableProperty is used to generate the property with INotifyPropertyChanged implementation which will notify the UI when the property changes
    [ObservableProperty]
    private bool _isAnyNodeLoading = false; // used for showing loading indicator on the UI
    
    [ObservableProperty]
    private long _selectedNodesSize = 0; // Total size of selected nodes
    
    private readonly DirectoryScanService _directoryScanService;
    private readonly IIconLoader _iconLoaderService;
    private ScanOptions? _currentScanOptions;
    private TopLevel? _topLevel;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    
    public MainWindowViewModel(DirectoryScanService directoryScanService, IIconLoader iconLoaderService)
    {
        _directoryScanService = directoryScanService;
        _iconLoaderService = iconLoaderService;
        
        // Wire up selection change tracking
        SelectedNodes.CollectionChanged += (s, e) =>
        {
            UpdateSelectedNodesSize();
        };
    }

    public MainWindowViewModel() { } // for designer
    
    private void UpdateSelectedNodesSize()
    {
        SelectedNodesSize = SelectedNodes.Sum(n => n.Size);
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
    }

    [RelayCommand]
    private async Task ShowScanOptionsWindowAsync()
    {
        if (_topLevel is null) 
            return;
        
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

                IsAnyNodeLoading = true;
                var sw = new Stopwatch();
                sw.Start();
                
                try
                {
                    // Load children for all roots in parallel - returns a dictionary mapping each root to its children
                    var childrenByRoot = await _directoryScanService.LoadChildrenForMultipleRootsAsync(validRoots, _currentScanOptions);

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var root in validRoots)
                        {
                            if (childrenByRoot.TryGetValue(root, out var children)) // if we got children for this root then add them to the root's Children collection
                            {
                                foreach (var child in children)
                                {
                                    root.Children.Add(child);
                                }
                                root.IsExpanded = true;
                            }
                        }
                    });
                }
                finally
                {
                    sw.Stop();
                    IsAnyNodeLoading = false;
                    Log.Information("Initial scan completed in {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                }
            }
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
}