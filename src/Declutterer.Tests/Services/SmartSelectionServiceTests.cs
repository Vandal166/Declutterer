using System;
using System.Linq;
using Declutterer.Models;
using Declutterer.Services;

namespace Declutterer.Tests.Services;

public class SmartSelectionServiceTests
{
    private readonly SmartSelectionScorer _scorer;
    private readonly SmartSelectionService _service;

    public SmartSelectionServiceTests()
    {
        _scorer = new SmartSelectionScorer();
        _service = new SmartSelectionService(_scorer);
    }

    [Fact]
    public void Select_EmptyTree_ReturnsEmptyList()
    {
        var root = CreateTreeNode("root", size: 1000);
        var scanOptions = CreateDefaultScanOptions();
        var scorerOptions = CreateDefaultScorerOptions();

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.Empty(result);
    }

    [Fact]
    public void Select_ExcludesRootNode()
    {
        var root = CreateTreeNode("root", size: 5000, lastModified: DateTime.UtcNow.AddMonths(-6));
        var child1 = CreateTreeNode("child1", size: 1000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-6));
        root.Children.Add(child1);

        var scanOptions = CreateScanOptionsWithAge();
        var scorerOptions = CreateDefaultScorerOptions();

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.DoesNotContain(root, result);
    }

    [Fact]
    public void Select_SelectsDirectChildrenOfRoot()
    {
        var root = CreateTreeNode("root", size: 10000);
        var child1 = CreateTreeNode("child1", size: 3000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-6));
        var child2 = CreateTreeNode("child2", size: 2000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-5));
        var grandchild = CreateTreeNode("grandchild", size: 1000, parent: child1, lastModified: DateTime.UtcNow.AddMonths(-7));
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild);

        var scanOptions = CreateScanOptionsWithAge();
        var scorerOptions = CreateDefaultScorerOptions();

        var result = _service.Select(root, scanOptions, scorerOptions);

        // Should only select direct children, not grandchildren
        Assert.All(result, node => Assert.Equal(root, node.Parent));
    }

    [Fact]
    public void Select_OrdersByScoreDescending()
    {
        var root = CreateTreeNode("root", size: 10000);
        // Create children with different sizes - larger size = higher score
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024, parent: root); // 1000 MB
        var child2 = CreateTreeNode("child2", size: 3000L * 1024 * 1024, parent: root); // 3000 MB (highest)
        var child3 = CreateTreeNode("child3", size: 2000L * 1024 * 1024, parent: root); // 2000 MB
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10 // 10 MB threshold
            }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 1.0 }; // Select all

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.Equal(3, result.Count);
        // Verify order: child2 (3000MB) -> child3 (2000MB) -> child1 (1000MB)
        Assert.Equal(child2, result[0]);
        Assert.Equal(child3, result[1]);
        Assert.Equal(child1, result[2]);
    }

    [Fact]
    public void Select_TopPercentage_SelectsCorrectCount()
    {
        var root = CreateTreeNode("root", size: 10000);
        var children = Enumerable.Range(0, 10)
            .Select(i => CreateTreeNode($"child{i}", size: (i + 1) * 100 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-i)))
            .ToList();
        
        foreach (var child in children)
        {
            root.Children.Add(child);
        }

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 0.4 }; // Select top 40%

        var result = _service.Select(root, scanOptions, scorerOptions);

        // 10 children * 0.4 = 4 children should be selected
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Select_TopPercentage_SelectsAtLeastOne()
    {
        var root = CreateTreeNode("root", size: 10000);
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024, parent: root);
        root.Children.Add(child1);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 0.1 }; // 10% of 1 = 0, but should be at least 1

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.Single(result);
        Assert.Contains(child1, result);
    }

    [Fact]
    public void Select_FiltersNestedItems()
    {
        var root = CreateTreeNode("root", size: 10000);
        
        // Create structure: root/parent/child
        var parent = CreateTreeNode("parent", size: 5000L * 1024 * 1024, parent: root);
        parent.FullPath = "/root/parent";
        
        var child = CreateTreeNode("child", size: 1000L * 1024 * 1024, parent: parent);
        child.FullPath = "/root/parent/child";
        
        root.Children.Add(parent);
        parent.Children.Add(child);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 1.0 };

        var result = _service.Select(root, scanOptions, scorerOptions);

        // Should only contain parent (direct child of root)
        Assert.Single(result);
        Assert.Contains(parent, result);
    }

    [Fact]
    public void Select_MultipleRootsWithDifferentScores_SelectsHighestScoring()
    {
        var root = CreateTreeNode("root", size: 10000);
        var child1 = CreateTreeNode("child1", size: 1000L * 1024 * 1024, parent: root);
        var child2 = CreateTreeNode("child2", size: 4000L * 1024 * 1024, parent: root);
        var child3 = CreateTreeNode("child3", size: 3000L * 1024 * 1024, parent: root);
        var child4 = CreateTreeNode("child4", size: 2000L * 1024 * 1024, parent: root);
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);
        root.Children.Add(child4);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 0.5 }; // Top 50%

        var result = _service.Select(root, scanOptions, scorerOptions);

        // 50% of 4 = 2 children
        Assert.Equal(2, result.Count);
        Assert.Contains(child2, result); // 4000MB
        Assert.Contains(child3, result); // 3000MB
        Assert.DoesNotContain(child4, result); // 2000MB
        Assert.DoesNotContain(child1, result); // 1000MB
    }

    [Fact]
    public void Select_EmptyChildren_ReturnsEmptyList()
    {
        var root = CreateTreeNode("root", size: 1000);
        var scanOptions = CreateDefaultScanOptions();
        var scorerOptions = CreateDefaultScorerOptions();

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.Empty(result);
    }

    [Fact]
    public void Select_WithAgeCriteria_SelectsOlderFiles()
    {
        var root = CreateTreeNode("root", size: 10000);
        var oldChild = CreateTreeNode("oldChild", size: 1000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-12));
        var newChild = CreateTreeNode("newChild", size: 1000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-1));
        
        root.Children.Add(oldChild);
        root.Children.Add(newChild);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 0.5 }; // Top 50%

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.Single(result);
        Assert.Contains(oldChild, result);
    }

    [Fact]
    public void Select_CombinedCriteria_SelectsBasedOnBoth()
    {
        var root = CreateTreeNode("root", size: 10000);
        // Old and large - should score highest
        var oldLarge = CreateTreeNode("oldLarge", size: 5000L * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-12));
        // Old but small
        var oldSmall = CreateTreeNode("oldSmall", size: 100L * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-12));
        // New but large
        var newLarge = CreateTreeNode("newLarge", size: 5000L * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-1));
        
        root.Children.Add(oldLarge);
        root.Children.Add(oldSmall);
        root.Children.Add(newLarge);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-6)
            },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 1000 // 1000 MB
            }
        };
        var scorerOptions = new ScorerOptions 
        { 
            TopPercentage = 0.34, // Should select 1 out of 3
            WeightAge = 0.5,
            WeightSize = 0.5
        };

        var result = _service.Select(root, scanOptions, scorerOptions);

        Assert.Single(result);
        Assert.Contains(oldLarge, result); // Should have highest combined score
    }

    // Helper methods
    private TreeNode CreateTreeNode(string name, long size, TreeNode? parent = null, DateTime? lastModified = null)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = $"/test/{name}",
            Size = size,
            IsDirectory = true,
            Parent = parent,
            LastModified = lastModified ?? DateTime.UtcNow
        };
    }

    private ScanOptions CreateDefaultScanOptions()
    {
        return new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
    }

    private ScanOptions CreateScanOptionsWithAge()
    {
        return new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
    }

    private ScorerOptions CreateDefaultScorerOptions()
    {
        return new ScorerOptions
        {
            WeightAge = 0.5,
            WeightSize = 0.5,
            TopPercentage = 0.4
        };
    }
}
