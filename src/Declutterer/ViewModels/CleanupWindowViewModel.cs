using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Common;
using Declutterer.Models;
using Declutterer.Services;

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
        ItemsToDelete.AddRange(itemsToDelete); //TODO in ui change "This will delete x item(s)" with "... + y item(s) from subfolders" so that the user is aware that also the children will be deleted and not just the top-level nodes they selected
        CalculateTotalSize();
        BuildGroupedItems();
    }
       
    public CleanupWindowViewModel() {} // for designer
    
    private void CalculateTotalSize()
    {
        long totalBytes = ItemsToDelete.Sum(item => item.Size);
        TotalSizeFormatted = ByteConverter.ToReadableString(totalBytes);
    }
    
    //TODO this has an issue:
    // this will build and group together directories that are nested eachother, example:
    // C:\Users\Kamilos\Downloads\Skyrim SE mods\mods\mods
    // and C:\Users\Kamilos\Downloads\Skyrim SE mods\mods
    // will be considered as separate items thus displaying double the amount of storage cleanup (MB/GB)
    // This coule be fixed by:
    // Changing the groupying where an 'Large direcotry' is considered only if it is the most-nested directory and exceeds the threshold OR the least-nested directory(the least as in from the root C:\Users\Kamilos\Downloads)
    // Example:
    // Most-nested is: C:\Users\Kamilos\Downloads\Skyrim SE mods\mods\mods\aMidianBorn Book of Silence SE\ - as this is the most nested directory that exceeds the threshold (size: 1.6 GB)
    // Least-nested: C:\Users\Kamilos\Downloads\Skyrim SE mods - this directory is least-nested and exceeds the threshold (size: 76 GB)
    private void BuildGroupedItems()
    {
        GroupedItems.Clear();
        
        var now = DateTime.Now;
        var largeFiles = new List<TreeNode>();
        var largeDirectories = new List<TreeNode>();
        var oldFiles = new List<TreeNode>();
        
        foreach (var item in ItemsToDelete)
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
                GroupName = "Large Directories (>100MB)",
                Items = new ObservableCollection<TreeNode>(largeDirectories)
            });
        }
        
        // Add Large Files group
        if (largeFiles.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = "Large Files (>100MB)",
                Items = new ObservableCollection<TreeNode>(largeFiles)
            });
        }
        
        // Add Old Files group
        if (oldFiles.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = "Old Files (Unused for 2+ years)",
                Items = new ObservableCollection<TreeNode>(oldFiles)
            });
        }
        
        // Add remaining items to "Other" group if not all items were categorized
        var categorizedItems = new HashSet<TreeNode>(largeFiles.Concat(largeDirectories).Concat(oldFiles));
        var otherItems = ItemsToDelete.Where(item => !categorizedItems.Contains(item)).ToList();
        
        if (otherItems.Count > 0)
        {
            GroupedItems.Add(new ItemGroup
            {
                GroupName = "Other Items",
                Items = new ObservableCollection<TreeNode>(otherItems)
            });
        }
    }
    
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;
}

/// <summary>
/// Represents a group of items with metadata about the group.
/// </summary>
public partial class ItemGroup : ObservableObject
{
    [ObservableProperty]
    private string groupName = string.Empty;
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> items = new();
    
    public string GroupSizeFormatted
    {
        get
        {
            long totalBytes = Items.Sum(item => item.Size);
            return ByteConverter.ToReadableString(totalBytes);
        }
    }
}