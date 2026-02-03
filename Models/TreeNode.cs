using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Declutterer.Models;

/// <summary>
/// The base model representing a file or directory in the tree.
/// TreeDataGrid automatically handles the hierarchy through the Children collection.
/// </summary>
public partial class TreeNode : ObservableObject
{
    // Static reference to the ViewModel's ToggleExpandCommand for lazy loading
    public static Func<TreeNode, System.Threading.Tasks.Task>? OnExpandRequested { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private long _size; // For files; for dirs: computed later

    [ObservableProperty]
    private DateTime? _lastModified;

    [ObservableProperty]
    private DateTime? _lastAccessed;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isSelected; // For checkbox multi-select

    [ObservableProperty]
    private bool _isExpanded; // Triggers lazy load

    [ObservableProperty]
    private bool _isLoading; // For spinner during load

    [ObservableProperty]
    private int _depth; // For tracking tree depth/indentation level

    [ObservableProperty]
    private bool _hasChildren; // Observable property for TreeDataGrid

    // Reference to parent node for tree traversal
    public TreeNode? Parent { get; set; }

    // Children (lazy-initialized) - TreeDataGrid reads this for hierarchical display
    private ObservableCollection<TreeNode> _children = new();
    public ObservableCollection<TreeNode> Children
    {
        get => _children;
        private set => _children = value;
    }

    // Optional: Icon kind or type (for future)
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(FullPath);

    public TreeNode()
    {
        // Subscribe to children collection changes to update HasChildren
        Children.CollectionChanged += OnChildrenChanged;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Update HasChildren whenever the collection changes
        HasChildren = IsDirectory && Children.Count > 0;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsDirectory && (Children.Count == 0 || (Children.Count == 1 && Children[0].Name == "Loading...")))
        {
            // Trigger lazy load through the callback
            OnExpandRequested?.Invoke(this);
        }
    }
}