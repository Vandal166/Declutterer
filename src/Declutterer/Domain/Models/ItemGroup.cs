using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Utilities.Helpers;

namespace Declutterer.Domain.Models;

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