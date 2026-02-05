using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Models;
using Declutterer.Services;
using Declutterer.Views;
using Serilog;

namespace Declutterer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    // ObservableProperty is used to generate the property with INotifyPropertyChanged implementation which will notify the UI when the property changes
    [ObservableProperty]
    private bool _isAnyNodeLoading = false; // used for showing loading indicator on the UI
    
    private readonly DirectoryScanService _directoryScanService;
    
    private ScanOptions? _currentScanOptions;
    private TopLevel? _topLevel;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public MainWindowViewModel(DirectoryScanService directoryScanService)
    {
        _directoryScanService = directoryScanService;
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

                // Clear existing roots
                Roots.Clear();

                // Add directories from scan options to Roots
                foreach (var directoryPath in _currentScanOptions.DirectoriesToScan.Where(Directory.Exists))
                {
                    var rootNode =  DirectoryScanService.CreateRootNode(directoryPath);
                    
                    Roots.Add(rootNode);
                }
                var sw = new Stopwatch();
                sw.Start();
                // Load children for all roots and expand them
                foreach (var root in Roots)
                {
                    await LoadChildrenForNodeAsync(root);
                    root.IsExpanded = true;
                }
                sw.Stop();
                Log.Information("Initial scan completed in {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            }
        }
    }
   
    [RelayCommand]
    private Task ToggleExpand(TreeNode node)
    {
        // Simply toggle the expanded state
        // The OnIsExpandedChanged partial method in TreeNode will handle lazy loading
        node.IsExpanded = !node.IsExpanded;
        return Task.CompletedTask;
    }
}