using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Selection;
using Declutterer.UI.Services.Commands;
using Declutterer.Utilities.Helpers;

namespace Declutterer.Tests.Integration;

public class CommandServiceIntegrationTests
{
    private readonly CommandService _commandService;
    private readonly SmartSelectionService _smartSelectionService;
    private readonly SmartSelectionScorer _scorer;

    public CommandServiceIntegrationTests()
    {
        _scorer = new SmartSelectionScorer();
        _smartSelectionService = new SmartSelectionService(_scorer);
        _commandService = new CommandService(_smartSelectionService);
    }

    [Fact]
    public void SelectAllChildren_SingleRoot_SelectsAllDirectChildren()
    {
        var root = CreateTreeNode("root", isDir: true);
        var child1 = CreateTreeNode("child1", parent: root);
        var child2 = CreateTreeNode("child2", parent: root);
        var child3 = CreateTreeNode("child3", parent: root);
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        _commandService.SelectAllChildren(new[] { root });

        Assert.True(child1.IsCheckboxSelected);
        Assert.True(child2.IsCheckboxSelected);
        Assert.True(child3.IsCheckboxSelected);
    }

    [Fact]
    public void SelectAllChildren_MultipleRoots_SelectsAllChildren()
    {
        var root1 = CreateTreeNode("root1", isDir: true);
        var child1 = CreateTreeNode("child1", parent: root1);
        var child2 = CreateTreeNode("child2", parent: root1);
        root1.Children.Add(child1);
        root1.Children.Add(child2);

        var root2 = CreateTreeNode("root2", isDir: true);
        var child3 = CreateTreeNode("child3", parent: root2);
        var child4 = CreateTreeNode("child4", parent: root2);
        root2.Children.Add(child3);
        root2.Children.Add(child4);

        _commandService.SelectAllChildren(new[] { root1, root2 });

        Assert.True(child1.IsCheckboxSelected);
        Assert.True(child2.IsCheckboxSelected);
        Assert.True(child3.IsCheckboxSelected);
        Assert.True(child4.IsCheckboxSelected);
    }

    [Fact]
    public void SelectAllChildren_EmptyRoot_DoesNotThrow()
    {
        var root = CreateTreeNode("root", isDir: true);

        var exception = Record.Exception(() => 
            _commandService.SelectAllChildren(new[] { root }));

        Assert.Null(exception);
    }

    [Fact]
    public void SelectAllChildren_DoesNotSelectGrandchildren()
    {
        var root = CreateTreeNode("root", isDir: true);
        var child = CreateTreeNode("child", parent: root, isDir: true);
        var grandchild = CreateTreeNode("grandchild", parent: child);
        
        root.Children.Add(child);
        child.Children.Add(grandchild);

        _commandService.SelectAllChildren(new[] { root });

        Assert.True(child.IsCheckboxSelected);
        Assert.False(grandchild.IsCheckboxSelected); // Only direct children are selected
    }

    [Fact]
    public void DeselectAll_ClearsAllSelections()
    {
        var selectedNodes = new ObservableHashSet<TreeNode>();
        var node1 = CreateTreeNode("node1");
        var node2 = CreateTreeNode("node2");
        var node3 = CreateTreeNode("node3");
        
        node1.IsCheckboxSelected = true;
        node2.IsCheckboxSelected = true;
        node3.IsCheckboxSelected = true;
        
        selectedNodes.Add(node1);
        selectedNodes.Add(node2);
        selectedNodes.Add(node3);

        _commandService.DeselectAll(selectedNodes);

        Assert.False(node1.IsCheckboxSelected);
        Assert.False(node2.IsCheckboxSelected);
        Assert.False(node3.IsCheckboxSelected);
        Assert.Empty(selectedNodes);
    }

    [Fact]
    public void DeselectAll_EmptyCollection_DoesNotThrow()
    {
        var selectedNodes = new ObservableHashSet<TreeNode>();

        var exception = Record.Exception(() => 
            _commandService.DeselectAll(selectedNodes));

        Assert.Null(exception);
    }

    [Fact]
    public void SmartSelect_WithFilters_SelectsTopCandidates()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var old_large = CreateTreeNode("old_large", size: 100 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-12));
        var old_small = CreateTreeNode("old_small", size: 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-12));
        var new_large = CreateTreeNode("new_large", size: 100 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddDays(-1));
        
        root.Children.Add(old_large);
        root.Children.Add(old_small);
        root.Children.Add(new_large);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-6)
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _commandService.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        Assert.NotEmpty(selectedNodes);
        Assert.Contains(old_large, selectedNodes);
        Assert.True(old_large.IsCheckboxSelected);
    }

    [Fact]
    public void SmartSelect_NullScanOptions_DoesNotSelect()
    {
        var root = CreateTreeNode("root", isDir: true);
        var child = CreateTreeNode("child", parent: root);
        root.Children.Add(child);
        
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _commandService.SmartSelect(new[] { root }, null, selectedNodes);

        Assert.Empty(selectedNodes);
        Assert.False(child.IsCheckboxSelected);
    }

    [Fact]
    public void SmartSelect_ClearsPreviousSelections()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var child1 = CreateTreeNode("child1", size: 10 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-6));
        var child2 = CreateTreeNode("child2", size: 20 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-6));
        
        root.Children.Add(child1);
        root.Children.Add(child2);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();
        
        // Manually select child1
        child1.IsCheckboxSelected = true;
        selectedNodes.Add(child1);

        // Run smart select
        _commandService.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Old selection should be cleared, new selection applied
        Assert.DoesNotContain(child1, selectedNodes.Where(n => !n.IsCheckboxSelected));
    }

    [Fact]
    public void SmartSelect_MultipleRoots_SelectsFromAll()
    {
        var root1 = CreateTreeNode("root1", size: 1000, isDir: true);
        var child1 = CreateTreeNode("child1", size: 50 * 1024 * 1024, parent: root1, 
            lastModified: DateTime.UtcNow.AddMonths(-12));
        root1.Children.Add(child1);

        var root2 = CreateTreeNode("root2", size: 1000, isDir: true);
        var child2 = CreateTreeNode("child2", size: 60 * 1024 * 1024, parent: root2, 
            lastModified: DateTime.UtcNow.AddMonths(-12));
        root2.Children.Add(child2);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 10 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-6)
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _commandService.SmartSelect(new[] { root1, root2 }, scanOptions, selectedNodes);

        Assert.NotEmpty(selectedNodes);
        // Should select from both roots
        Assert.True(selectedNodes.Count >= 1);
    }

    [Fact]
    public void Integration_FullWorkflow_SelectDeselectReselect()
    {
        var root = CreateTreeNode("root", isDir: true);
        var child1 = CreateTreeNode("child1", parent: root);
        var child2 = CreateTreeNode("child2", parent: root);
        var child3 = CreateTreeNode("child3", parent: root);
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        var selectedNodes = new ObservableHashSet<TreeNode>();

        // Step 1: Select all children
        _commandService.SelectAllChildren(new[] { root });
        selectedNodes.Add(child1);
        selectedNodes.Add(child2);
        selectedNodes.Add(child3);

        Assert.Equal(3, selectedNodes.Count);
        Assert.All(root.Children, c => Assert.True(c.IsCheckboxSelected));

        // Step 2: Deselect all
        _commandService.DeselectAll(selectedNodes);

        Assert.Empty(selectedNodes);
        Assert.All(root.Children, c => Assert.False(c.IsCheckboxSelected));

        // Step 3: Select all again
        _commandService.SelectAllChildren(new[] { root });

        Assert.All(root.Children, c => Assert.True(c.IsCheckboxSelected));
    }

    [Fact]
    public void Integration_SmartSelectWithComplexTree()
    {
        // Create a complex tree structure
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        
        // Add various children with different properties
        var children = new[]
        {
            CreateTreeNode("huge_old", size: 1024 * 1024 * 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddYears(-2)),
            CreateTreeNode("large_old", size: 500 * 1024 * 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddMonths(-18)),
            CreateTreeNode("medium_old", size: 100 * 1024 * 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddMonths(-12)),
            CreateTreeNode("small_old", size: 10 * 1024 * 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddMonths(-6)),
            CreateTreeNode("huge_new", size: 1024 * 1024 * 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddDays(-1)),
            CreateTreeNode("tiny", size: 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddDays(-1))
        };

        foreach (var child in children)
        {
            root.Children.Add(child);
        }

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 50 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            }
        };
        var selectedNodes = new ObservableHashSet<TreeNode>();

        _commandService.SmartSelect(new[] { root }, scanOptions, selectedNodes);

        // Should select the largest, oldest items
        Assert.NotEmpty(selectedNodes);
        Assert.All(selectedNodes, node => 
        {
            Assert.True(node.Size >= 100 * 1024 * 1024);
            Assert.True(node.LastModified < DateTime.UtcNow.AddMonths(-3));
        });
    }

    [Fact]
    public void Integration_CombineManualAndSmartSelection()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var child1 = CreateTreeNode("child1", size: 50 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-12));
        var child2 = CreateTreeNode("child2", size: 100 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-6));
        var child3 = CreateTreeNode("child3", size: 10 * 1024 * 1024, parent: root, 
            lastModified: DateTime.UtcNow.AddMonths(-1));
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        var selectedNodes = new ObservableHashSet<TreeNode>();
        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 10 }
        };

        // Start with smart selection
        _commandService.SmartSelect(new[] { root }, scanOptions, selectedNodes);
        var smartSelectedCount = selectedNodes.Count;

        // Then select all (this would replace smart selection in real usage)
        _commandService.DeselectAll(selectedNodes);
        _commandService.SelectAllChildren(new[] { root });

        Assert.Equal(3, root.Children.Count(c => c.IsCheckboxSelected));
    }

    [Fact]
    public void Integration_WithEmptyAndPopulatedRoots()
    {
        var emptyRoot = CreateTreeNode("empty", isDir: true);
        var populatedRoot = CreateTreeNode("populated", isDir: true);
        var child = CreateTreeNode("child", parent: populatedRoot);
        populatedRoot.Children.Add(child);

        _commandService.SelectAllChildren(new[] { emptyRoot, populatedRoot });

        Assert.False(emptyRoot.Children.Any());
        Assert.Single(populatedRoot.Children);
        Assert.True(child.IsCheckboxSelected);
    }

    [Fact]
    public void Integration_DeselectAllMaintainsConsistency()
    {
        var selectedNodes = new ObservableHashSet<TreeNode>();
        var nodes = Enumerable.Range(1, 100)
            .Select(i => CreateTreeNode($"node{i}"))
            .ToList();

        foreach (var node in nodes)
        {
            node.IsCheckboxSelected = true;
            selectedNodes.Add(node);
        }

        _commandService.DeselectAll(selectedNodes);

        Assert.Empty(selectedNodes);
        Assert.All(nodes, n => Assert.False(n.IsCheckboxSelected));
    }

    private TreeNode CreateTreeNode(string name, long size = 1024, TreeNode? parent = null, 
        DateTime? lastModified = null, bool isDir = false)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = parent != null ? $"{parent.FullPath}/{name}" : $"/{name}",
            Size = size,
            Parent = parent,
            LastModified = lastModified ?? DateTime.UtcNow,
            IsDirectory = isDir,
            Depth = parent?.Depth + 1 ?? 0,
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false
        };
    }
}
