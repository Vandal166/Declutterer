using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;
using Serilog;

namespace Declutterer.ViewModels;

public sealed partial class CleanupWindowViewModel : ViewModelBase, IContextMenuProvider
{
    private const long LargeFileSizeThresholdBytes = 100 * 1024 * 1024; // 100 MB
    private static readonly TimeSpan OldFileThreshold = TimeSpan.FromDays(365 * 2); // 2 years
    
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IErrorDialogService _errorDialogService;
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _itemsToDelete = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _largeDirectories = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _largeFiles = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _oldFiles = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _otherItems = new();
    
    [ObservableProperty]
    private string _largeDirectoriesSize = "0 B";
    
    [ObservableProperty]
    private string _largeFilesSize = "0 B";
    
    [ObservableProperty]
    private string _oldFilesSize = "0 B";
    
    [ObservableProperty]
    private string _otherItemsSize = "0 B";
    
    [ObservableProperty]
    private bool _sendToRecycleBin = true; // Default to safer option
    
    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isDeletionInProgress = false;
    
    [ObservableProperty]
    private double _deletionProgress = 0; // 0 to 100
    
    [ObservableProperty]
    private string _deletionStatus = string.Empty;
    
    [ObservableProperty]
    private TreeNode? _selectedItem = null;
    
    private TopLevel? _topLevel; // Reference to the TopLevel window for folder picker
    
    [ObservableProperty]
    private bool _canDelete;
    
    public CleanupWindowViewModel(ObservableCollection<TreeNode> itemsToDelete, IExplorerLauncher explorerLauncher, IErrorDialogService errorDialogService)
    {
        _explorerLauncher = explorerLauncher;
        _errorDialogService = errorDialogService;
        
        ItemsToDelete.CollectionChanged += ItemsToDeleteOnCollectionChanged;
        
        ItemsToDelete.Clear();
        foreach (var item in itemsToDelete)
        {
            ItemsToDelete.Add(item);
        }
        
        CalculateTotalSize();
        BuildGroupedItems();
    }

    private void ItemsToDeleteOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CanDelete = ItemsToDelete.Count > 0;
    }

    public CleanupWindowViewModel() {} // for designer
    
    private void CalculateTotalSize()
    {
        // Filter to only top-level items (exclude items nested within other items)
        var topLevelItems = TreeNodeHelper.GetTopLevelItems(ItemsToDelete);
        long totalBytes = topLevelItems.Sum(item => item.Size);
        TotalSizeFormatted = ByteConverter.ToReadableString(totalBytes);
    }
    
    private void BuildGroupedItems()
    {
        LargeDirectories.Clear();
        LargeFiles.Clear();
        OldFiles.Clear();
        OtherItems.Clear();
        
        if(ItemsToDelete.Count == 0)
            return;
        
        // Filter to only top-level items (exclude items nested within other items)
        // This prevents double-counting sizes when both a parent and child directory are selected
        var topLevelItems = TreeNodeHelper.GetTopLevelItems(ItemsToDelete);
        
        var now = DateTime.Now;
        var largeFilesList = new List<TreeNode>();
        var largeDirectoriesList = new List<TreeNode>();
        var oldFilesList = new List<TreeNode>();
        
        foreach (var item in topLevelItems)
        {
            if (item.Size >= LargeFileSizeThresholdBytes)
            {
                if (item.IsDirectory)
                {
                    largeDirectoriesList.Add(item);
                }
                else
                {
                    largeFilesList.Add(item);
                }
            }
            
            if (item.LastModified.HasValue && (now - item.LastModified.Value) > OldFileThreshold)
            {
                oldFilesList.Add(item);
            }
        }
        
        // Populate Large Directories collection
        foreach (var item in largeDirectoriesList.OrderByDescending(d => d.Size))
        {
            LargeDirectories.Add(item);
        }
        LargeDirectoriesSize = ByteConverter.ToReadableString(largeDirectoriesList.Sum(d => d.Size));
        
        // Populate Large Files collection
        foreach (var item in largeFilesList.OrderByDescending(f => f.Size))
        {
            LargeFiles.Add(item);
        }
        LargeFilesSize = ByteConverter.ToReadableString(largeFilesList.Sum(f => f.Size));
        
        // Populate Old Files collection
        foreach (var item in oldFilesList.OrderBy(f => f.LastModified)) // Oldest first
        {
            OldFiles.Add(item);
        }
        OldFilesSize = ByteConverter.ToReadableString(oldFilesList.Sum(f => f.Size));
        
        // Populate Other Items collection
        var categorizedItems = new HashSet<TreeNode>(largeFilesList.Concat(largeDirectoriesList).Concat(oldFilesList));
        var otherItemsList = topLevelItems.Where(item => !categorizedItems.Contains(item)).ToList();
        
        foreach (var item in otherItemsList)
        {
            OtherItems.Add(item);
        }
        OtherItemsSize = ByteConverter.ToReadableString(otherItemsList.Sum(i => i.Size));
    }
    
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;
    
    [RelayCommand]
    private void RemoveFromCleanup(TreeNode? item)
    {
        if (item is null) 
            return;
        
        // Remove the item from _itemsToDelete
        ItemsToDelete.Remove(item);
      
        // Recalculate total size
        CalculateTotalSize();
        
        BuildGroupedItems();
        
        // Clear selection
        SelectedItem = null;
    }

     [RelayCommand]
     private async Task ContextMenuSelect(TreeNode? node)
     {
         // For cleanup window, "select" means removing from cleanup or marking differently
         // For now, we'll just toggle it or you can adapt this as needed
         if (node is not null)
         {
             RemoveFromCleanup(node);
         }
         await Task.CompletedTask;
     }

     [RelayCommand]
     private async Task ContextMenuOpenInExplorer(TreeNode? node)
     {
         try
         {
             if (node is null)
                 return;

             _explorerLauncher.OpenInExplorer(node.FullPath);
         }
         catch (Exception e)
         {
             Log.Error(e, "Failed to open node in explorer: {NodePath}", node?.FullPath);
             await _errorDialogService.ShowErrorAsync(
                 "Failed to Open in Explorer",
                 $"Could not open the path in File Explorer:\n{node?.FullPath}",
                 e);
         }
     }

     [RelayCommand]
     private async Task ContextMenuCopyPath(TreeNode? node)
     {
         try
         {
             if (node is null)
                 return;

             if (_topLevel?.Clipboard is IClipboard clipboard)
             {
                 await clipboard.SetTextAsync(node.FullPath);
             }
         }
         catch (Exception e)
         {
             Log.Error(e, "Failed to copy path to clipboard: {NodePath}", node?.FullPath);
             await _errorDialogService.ShowErrorAsync(
                 "Failed to Copy Path",
                 $"Could not copy the path to clipboard:\n{node?.FullPath}",
                 e);
         }
     }
}
