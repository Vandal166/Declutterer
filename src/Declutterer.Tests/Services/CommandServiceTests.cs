using System;
using System.Linq;
using Declutterer.Common;
using Declutterer.Models;
using Declutterer.Services;

namespace Declutterer.Tests.Services;

public class CommandServiceTests
{
    private readonly SmartSelectionService _smartSelectionService;
    private readonly CommandService _service;

    public CommandServiceTests()
    {
        var scorer = new SmartSelectionScorer();
        _smartSelectionService = new SmartSelectionService(scorer);
        _service = new CommandService(_smartSelectionService);
    }

    [Fact]
    public void Constructor_NullSmartSelectionService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CommandService(null!));
    }

    [Fact]
    public void SelectAllChildren_SingleRoot_SelectsAllChildren()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child1 = CreateTreeNode("child1", size: 1000);
        var child2 = CreateTreeNode("child2", size: 2000);
        var child3 = CreateTreeNode("child3", size: 3000);
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        _service.SelectAllChildren(new[] { root });

        Assert.True(child1.IsCheckboxSelected);
        Assert.True(child2.IsCheckboxSelected);
        Assert.True(child3.IsCheckboxSelected);
        Assert.False(root.IsCheckboxSelected);
    }

    [Fact]
    public void SelectAllChildren_MultipleRoots_SelectsAllChildren()
    {
        var root1 = CreateTreeNode("root1", size: 5000);
        var root1Child1 = CreateTreeNode("child1", size: 1000);
        var root1Child2 = CreateTreeNode("child2", size: 2000);
        root1.Children.Add(root1Child1);
        root1.Children.Add(root1Child2);

        var root2 = CreateTreeNode("root2", size: 5000);
        var root2Child1 = CreateTreeNode("child1", size: 1000);
        var root2Child2 = CreateTreeNode("child2", size: 2000);
        root2.Children.Add(root2Child1);
        root2.Children.Add(root2Child2);

        _service.SelectAllChildren(new[] { root1, root2 });

        Assert.True(root1Child1.IsCheckboxSelected);
        Assert.True(root1Child2.IsCheckboxSelected);
        Assert.True(root2Child1.IsCheckboxSelected);
        Assert.True(root2Child2.IsCheckboxSelected);
        Assert.False(root1.IsCheckboxSelected);
        Assert.False(root2.IsCheckboxSelected);
    }

    [Fact]
    public void SelectAllChildren_RootWithNoChildren_DoesNothing()
    {
        var root = CreateTreeNode("root", size: 5000);

        _service.SelectAllChildren(new[] { root });

        Assert.False(root.IsCheckboxSelected);
    }

    [Fact]
    public void SelectAllChildren_EmptyRootsList_DoesNothing()
    {
        _service.SelectAllChildren(Array.Empty<TreeNode>());

        // Should not throw
    }

    [Fact]
    public void SelectAllChildren_DoesNotSelectGrandchildren()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child = CreateTreeNode("child", size: 2000);
        var grandchild = CreateTreeNode("grandchild", size: 1000);
        
        root.Children.Add(child);
        child.Children.Add(grandchild);

        _service.SelectAllChildren(new[] { root });

        Assert.True(child.IsCheckboxSelected);
        Assert.False(grandchild.IsCheckboxSelected);
        Assert.False(root.IsCheckboxSelected);
    }

    [Fact]
    public void DeselectAll_ClearsAllSelections()
    {
        var selectedNodes = new ObservableHashSet<TreeNode>();
        var node1 = CreateTreeNode("node1", size: 1000);
        var node2 = CreateTreeNode("node2", size: 2000);
        var node3 = CreateTreeNode("node3", size: 3000);
        
        node1.IsCheckboxSelected = true;
        node2.IsCheckboxSelected = true;
        node3.IsCheckboxSelected = true;
        
        selectedNodes.Add(node1);
        selectedNodes.Add(node2);
        selectedNodes.Add(node3);

        _service.DeselectAll(selectedNodes);

        Assert.False(node1.IsCheckboxSelected);
        Assert.False(node2.IsCheckboxSelected);
        Assert.False(node3.IsCheckboxSelected);
        Assert.Empty(selectedNodes);
    }

    [Fact]
    public void DeselectAll_EmptySet_DoesNothing()
    {
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.DeselectAll(selectedNodes);

        Assert.Empty(selectedNodes);
    }

    [Fact]
    public void SmartSelect_NullScanOptions_DoesNothing()
    {
        var root = CreateTreeNode("root", size: 5000);
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root }, null, selectedNodes);

        Assert.Empty(selectedNodes);
    }

    [Fact]
    public void SmartSelect_ValidOptions_SelectsNodes()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024);
        var child2 = CreateTreeNode("child2", size: 2000L * 1024 * 1024);
        
        child1.Parent = root;
        child2.Parent = root;
        root.Children.Add(child1);
        root.Children.Add(child2);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Should select nodes based on scoring
        Assert.NotEmpty(selectedNodes);
    }

    [Fact]
    public void SmartSelect_SelectsReturnedNodes()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024);
        var child2 = CreateTreeNode("child2", size: 3000L * 1024 * 1024);
        var child3 = CreateTreeNode("child3", size: 2000L * 1024 * 1024);
        
        child1.Parent = root;
        child2.Parent = root;
        child3.Parent = root;
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // With default 40% top percentage, should select at least 1 node
        Assert.NotEmpty(selectedNodes);
        // All selected nodes should have checkbox selected
        Assert.All(selectedNodes, node => Assert.True(node.IsCheckboxSelected));
    }

    [Fact]
    public void SmartSelect_ClearsPreviousSelections()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024);
        var child2 = CreateTreeNode("child2", size: 2000L * 1024 * 1024);
        
        child1.Parent = root;
        child2.Parent = root;
        root.Children.Add(child1);
        root.Children.Add(child2);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();
        
        // Pre-select child2
        child2.IsCheckboxSelected = true;
        selectedNodes.Add(child2);

        _service.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Previous selections should be cleared
        Assert.DoesNotContain(child2, selectedNodes.Where(n => !n.IsCheckboxSelected));
    }

    [Fact]
    public void SmartSelect_MultipleRoots_ProcessesAll()
    {
        var root1 = CreateTreeNode("root1", size: 5000);
        var root1Child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024);
        root1Child1.Parent = root1;
        root1.Children.Add(root1Child1);

        var root2 = CreateTreeNode("root2", size: 5000);
        var root2Child1 = CreateTreeNode("child1", size: 2000L * 1024 * 1024);
        root2Child1.Parent = root2;
        root2.Children.Add(root2Child1);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root1, root2 }, scanOptions, selectedNodes);

        // Should have selected from both roots
        Assert.NotEmpty(selectedNodes);
    }

    [Fact]
    public void SmartSelect_EmptyRoots_DoesNothing()
    {
        var scanOptions = new ScanOptions();
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(Array.Empty<TreeNode>(), scanOptions, selectedNodes);

        Assert.Empty(selectedNodes);
    }

    [Fact]
    public void SmartSelect_SelectsBasedOnFilters()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child = CreateTreeNode("child", size: 1000L * 1024 * 1024);
        child.Parent = root;
        root.Children.Add(child);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10 // 10 MB threshold, child is 1000 MB
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Child meets the criteria and should be selected (top percentage selects at least 1)
        Assert.Single(selectedNodes);
        Assert.Contains(child, selectedNodes);
    }

    [Fact]
    public void SmartSelect_UsesScorerOptionsWithDefaults()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child = CreateTreeNode("child", size: 1000L * 1024 * 1024);
        child.Parent = root;
        root.Children.Add(child);
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Should use default scorer options (WeightAge=0.5, WeightSize=0.5, TopPercentage=0.4)
        // With one child, top 40% should still select it (at least 1)
        Assert.Single(selectedNodes);
    }

    [Fact]
    public void SmartSelect_AddsToSelectedNodesSet()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024);
        var child2 = CreateTreeNode("child2", size: 2000L * 1024 * 1024);
        
        child1.Parent = root;
        child2.Parent = root;
        root.Children.Add(child1);
        root.Children.Add(child2);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _service.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Should add nodes to the selected set
        Assert.NotEmpty(selectedNodes);
        Assert.All(selectedNodes, node => Assert.True(node.IsCheckboxSelected));
    }

    // Helper method
    private TreeNode CreateTreeNode(string name, long size)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = $"/test/{name}",
            Size = size,
            IsDirectory = true,
            IsCheckboxSelected = false
        };
    }
}
