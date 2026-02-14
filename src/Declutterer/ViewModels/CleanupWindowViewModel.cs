using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.ViewModels;

public sealed partial class CleanupWindowViewModel : ViewModelBase
{
    private const long LargeFileSizeThresholdBytes = 100 * 1024 * 1024; // 100 MB
    private static readonly TimeSpan OldFileThreshold = TimeSpan.FromDays(365 * 2); // 2 years
    
    public List<TreeNode> ItemsToDelete { get; } = new();
    
    [ObservableProperty]
    private ObservableCollection<ItemGroup> groupedItems = new();
    
    [ObservableProperty]
    private bool sendToRecycleBin = true; // Default to safer option
    
    [ObservableProperty]
    private string totalSizeFormatted = "0 B";
    
    [ObservableProperty]
    private bool isDeletionInProgress = false;
    
    [ObservableProperty]
    private double deletionProgress = 0; // 0 to 100
    
    [ObservableProperty]
    private string deletionStatus = string.Empty;
    
    private TopLevel? _topLevel; // Reference to the TopLevel window for folder picker
    
    public CleanupWindowViewModel(List<TreeNode> itemsToDelete)
    {
        ItemsToDelete.Clear();
        ItemsToDelete.AddRange(itemsToDelete);
        CalculateTotalSize();
        BuildGroupedItems();
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
                Items = new ObservableCollection<TreeNode>(largeDirectories)
            });
        }
        
        // Add Large Files group
        if (largeFiles.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = $"{largeFiles.Count} Large Files (>100MB)",
                Items = new ObservableCollection<TreeNode>(largeFiles)
            });
        }
        
        // Add Old Files group
        if (oldFiles.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = $"{oldFiles.Count} Old Files (Last Modified > 2 years ago)",
                Items = new ObservableCollection<TreeNode>(oldFiles)
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
}