using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Models;
using Declutterer.Services;

namespace Declutterer.ViewModels;

public sealed class CleanupWindowViewModel : ViewModelBase
{
    public List<TreeNode> ItemsToDelete { get; }
    
    private TopLevel? _topLevel; // Reference to the TopLevel window for folder picker
    public CleanupWindowViewModel(List<TreeNode> itemsToDelete)
    {
        ItemsToDelete = itemsToDelete;
    }
       
    public CleanupWindowViewModel() {} // for designer
    
    
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

}