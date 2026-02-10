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
    private bool _noChildrenFound = false; // flag to indicate that no children were found for the selected directories based on the scan options
    
    [ObservableProperty]
    private string _selectedNodesSizeText = string.Empty; // a user-friendly string representation of the total size of the currently selected nodes
    
    private readonly DirectoryScanService _directoryScanService;
    private readonly SmartSelectionService _smartSelectionService;
    private readonly IIconLoader _iconLoaderService;
    
    private ScanOptions? _currentScanOptions;
    private TopLevel? _topLevel;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    
    public MainWindowViewModel(DirectoryScanService directoryScanService, IIconLoader iconLoaderService, SmartSelectionService smartSelectionService)
    {
        _directoryScanService = directoryScanService;
        _iconLoaderService = iconLoaderService;
        _smartSelectionService = smartSelectionService;

        // Wire up selection change tracking
        SelectedNodes.CollectionChanged += (s, e) =>
        {
            UpdateSelectedNodesSize();
        };
    }

    public MainWindowViewModel() { } // for designer
    
    private void UpdateSelectedNodesSize()
    {
        var selectedNodesSize = SelectedNodes.Sum(n => n.Size);
        SelectedNodesSizeText = ByteConverter.ToReadableString(selectedNodesSize);
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
        NoChildrenFound = false;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var node in SelectedNodes.ToList())
        {
            node.IsSelected = false; // This will trigger the UI to uncheck the node and also update the SelectedNodes collection through the binding
        }
        SelectedNodes.Clear();
    }
    
    [RelayCommand]
    private void SmartSelect()
    {
        if (_currentScanOptions is null)
            return;
        
        DeselectAll();
        foreach (var rootChild in Roots)
        {
            var toSelect = _smartSelectionService.Select(rootChild, _currentScanOptions, new ScorerOptions());
            foreach (var selectedNode in toSelect)
            {
                selectedNode.IsSelected = true; // Update the node's selection state for UI binding
                SelectedNodes.Add(selectedNode);
            }
        }
    }
    
    [RelayCommand]
    private async Task ShowScanOptionsWindowAsync()
    {
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
                NoChildrenFound = false;

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

                    if (childrenByRoot.Values.All(children => children.Count == 0))
                    {
                        // No children were found for the selected directories based on the scan options
                        NoChildrenFound = true;
                        return;
                    }
                    
                    // Reset the flag since we found children
                    NoChildrenFound = false;
                    
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