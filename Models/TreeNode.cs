using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Common;

namespace Declutterer.Models;

public partial class AgeFilter : ObservableObject
{
    [ObservableProperty]
    private int _monthsModifiedValue = 1; // storing months value

    [ObservableProperty]
    private DateTime? _modifiedBefore = null;

    [ObservableProperty]
    private bool _useModifiedDate = false; // Whether to apply modified date filter
    
    partial void OnMonthsAccessedValueChanged(int value)
    {
        // Auto-enable filter when user changes the months value
        if (value > 0)
        {
            UseAccessedDate = true;
        }
    }
    
    [ObservableProperty]
    private DateTime? _accessedBefore = null;
    
    [ObservableProperty]
    private int _monthsAccessedValue = 1;
    
    [ObservableProperty]
    private bool _useAccessedDate = false; // Whether to apply accessed date filter
    
    partial void OnMonthsModifiedValueChanged(int value)
    {
        // Auto-enable filter when user changes the months value
        if (value > 0)
        {
            UseModifiedDate = true;
        }
    }
}

// not only for directories but also for files, so we can filter out large files if needed(only if include files is checked)
public partial class EntrySizeFilter : ObservableObject
{
    // TODO mby use MinMax slider
    [ObservableProperty]
    private long _sizeThreshold = 1; // in MB
    
    [ObservableProperty]
    private bool _useSizeFilter = false; // Whether to apply size filter
    
    partial void OnSizeThresholdChanged(long value)
    {
        // Auto-enable filter when user changes the threshold
        if (value > 0)
        {
            UseSizeFilter = true;
        }
    }
}

public partial class ScanOptions : ObservableObject
{
    [ObservableProperty]
    private ObservableHashSet<string> _directoriesToScan = new();
    
    [ObservableProperty]
    private AgeFilter _ageFilter = new();
    
    [ObservableProperty]
    private EntrySizeFilter _entrySizeFilter = new();

}


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
}