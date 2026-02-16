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
    private ObservableCollection<ItemGroup> _groupedItems = new();
    
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
        GroupedItems.Clear();
        
        if(ItemsToDelete.Count == 0)
            return; //TODO show "No items to delete" message instead of empty groups
        
        // Filter to only top-level items (exclude items nested within other items)
        // This prevents double-counting sizes when both a parent and child directory are selected
        var topLevelItems = TreeNodeHelper.GetTopLevelItems(ItemsToDelete);
        
        var now = DateTime.Now;
        var largeFiles = new List<TreeNode>();
        var largeDirectories = new List<TreeNode>();
        var oldFiles = new List<TreeNode>();
        
        foreach (var item in topLevelItems)
        {
            if (item.Size >= LargeFileSizeThresholdBytes)
            {
                if (item.IsDirectory)
                {
                    largeDirectories.Add(item);
                }
                else
                {
                    largeFiles.Add(item);
                }
            }
            
            if (item.LastModified.HasValue && (now - item.LastModified.Value) > OldFileThreshold)
            {
                oldFiles.Add(item);
            }
        }
        
        
        // Add Large Directories group
        if (largeDirectories.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = $"{largeDirectories.Count} Large Directories (>100MB)",
                Items = new ObservableCollection<TreeNode>(largeDirectories.OrderByDescending(d => d.Size))
            });
        }
        
        // Add Large Files group
        if (largeFiles.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = $"{largeFiles.Count} Large Files (>100MB)",
                Items = new ObservableCollection<TreeNode>(largeFiles.OrderByDescending(f => f.Size))
            });
        }
        
        // Add Old Files group
        if (oldFiles.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = $"{oldFiles.Count} Old Files (Last Modified > 2 years ago)",
                Items = new ObservableCollection<TreeNode>(oldFiles.OrderBy(f => f.LastModified)) // Oldest first
            });
        }
        
        // Add remaining items to "Other" group if not all items were categorized
        var categorizedItems = new HashSet<TreeNode>(largeFiles.Concat(largeDirectories).Concat(oldFiles));
        var otherItems = topLevelItems.Where(item => !categorizedItems.Contains(item)).ToList();
        
        if (otherItems.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = $"{otherItems.Count} Other Items",
                Items = new ObservableCollection<TreeNode>(otherItems)
            });
        }
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
     private Task ContextMenuSelect(TreeNode? node)
     {
         // For cleanup window, "select" means removing from cleanup or marking differently
         // For now, we'll just toggle it or you can adapt this as needed
         if (node is not null)
         {
             RemoveFromCleanup(node);
         }
         return Task.CompletedTask;
     }

     [RelayCommand]
     private Task ContextMenuOpenInExplorer(TreeNode? node)
     {
         try
         {
             if (node is null)
                 return Task.CompletedTask;

             _explorerLauncher.OpenInExplorer(node.FullPath);
         }
         catch (Exception e)
         {
             Log.Error(e, "Failed to open node in explorer: {NodePath}", node?.FullPath);
             _ = _errorDialogService.ShowErrorAsync(
                 "Failed to Open in Explorer",
                 $"Could not open the path in File Explorer:\n{node?.FullPath}",
                 e);
         }
         return Task.CompletedTask;
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
