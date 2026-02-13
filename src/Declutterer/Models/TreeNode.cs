using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Common;

namespace Declutterer.Models;

/// <summary>
/// The base model representing a file or directory in the tree.
/// TreeDataGrid automatically handles the hierarchy through the Children collection.
/// </summary>
public partial class TreeNode : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private long _size; // size in bytes

    public string SizeFormatted => ByteConverter.ToReadableString(Size);
    
    [ObservableProperty]
    private DateTime? _lastModified;

    [ObservableProperty]
    private DateTime? _lastAccessed;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isCheckboxSelected; // For checkbox multi-select
    
    [ObservableProperty]
    private bool _isCheckboxEnabled = true; // disable/enable state of the UI checkbox

    [ObservableProperty]
    private bool _isExpanded; // Triggers lazy load

    [ObservableProperty]
    private int _depth; // For tracking tree depth/indentation level

    [ObservableProperty]
    private bool _hasChildren; // Observable property for TreeDataGrid

    // Reference to parent node for tree traversal
    public TreeNode? Parent { get; set; }

    // Children (lazy-initialized) - TreeDataGrid reads this for hierarchical display
    public ObservableCollection<TreeNode> Children { get; private set; } = new();

    [ObservableProperty]
    private Bitmap? _icon = null; // The icon associated with this node (file or folder), loaded asynchronously could be null
}