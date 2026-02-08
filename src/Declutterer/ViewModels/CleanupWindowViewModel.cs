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
    public List<TreeNode> ItemsToDelete { get; } = new();
    
    [ObservableProperty]
    private bool sendToRecycleBin = true; // Default to safer option
    
    [ObservableProperty]
    private string totalSizeFormatted = "0 B";
    
    private TopLevel? _topLevel; // Reference to the TopLevel window for folder picker
    
    public CleanupWindowViewModel(List<TreeNode> itemsToDelete)
    {
        ItemsToDelete.Clear();
        ItemsToDelete.AddRange(itemsToDelete); //TODO in ui change "This will delete x item(s)" with "... + y item(s) from subfolders" so that the user is aware that also the children will be deleted and not just the top-level nodes they selected
        CalculateTotalSize();
    }
       
    public CleanupWindowViewModel() {} // for designer
    
    private void CalculateTotalSize()
    {
        long totalBytes = ItemsToDelete.Sum(item => item.Size);
        TotalSizeFormatted = ConvertBytes.ToReadableString(totalBytes);
    }
    
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

}