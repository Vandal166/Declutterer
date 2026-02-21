using Declutterer.Domain.Models;
using Declutterer.Utilities.Helpers;

namespace Declutterer.Tests.Common;

public class TreeNodeHelperTests
{
    private static string MakePath(params string[] parts)
    {
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    [Fact]
    public void GetTopLevelItems_EmptyList_ReturnsEmptyList()
    {
        var items = new List<TreeNode>();
        var result = TreeNodeHelper.GetTopLevelItems(items);
        Assert.Empty(result);
    }

    [Fact]
    public void GetTopLevelItems_SingleItem_ReturnsSingleItem()
    {
        var path = MakePath("", "folder");
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = path }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Single(result);
        Assert.Equal(path, result[0].FullPath);
    }

    [Fact]
    public void GetTopLevelItems_ParentAndChild_ReturnsOnlyParent()
    {
        var parent = MakePath("", "parent");
        var child = MakePath("", "parent", "child");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = parent },
            new TreeNode { FullPath = child }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Single(result);
        Assert.Equal(parent, result[0].FullPath);
    }

    [Fact]
    public void GetTopLevelItems_ChildListedFirst_StillReturnsOnlyParent()
    {
        var parent = MakePath("", "parent");
        var child = MakePath("", "parent", "child");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = child },
            new TreeNode { FullPath = parent }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Single(result);
        Assert.Equal(parent, result[0].FullPath);
    }

    [Fact]
    public void GetTopLevelItems_MultipleLevelsNested_ReturnsOnlyTopLevel()
    {
        var a = MakePath("", "a");
        var ab = MakePath("", "a", "b");
        var abc = MakePath("", "a", "b", "c");
        var abcd = MakePath("", "a", "b", "c", "d");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = a },
            new TreeNode { FullPath = ab },
            new TreeNode { FullPath = abc },
            new TreeNode { FullPath = abcd }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Single(result);
        Assert.Equal(a, result[0].FullPath);
    }

    [Fact]
    public void GetTopLevelItems_SeparateBranches_ReturnsAllBranches()
    {
        var folder1 = MakePath("", "folder1");
        var folder2 = MakePath("", "folder2");
        var folder3 = MakePath("", "folder3");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = folder1 },
            new TreeNode { FullPath = folder2 },
            new TreeNode { FullPath = folder3 }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Equal(3, result.Count);
        Assert.Contains(result, n => n.FullPath == folder1);
        Assert.Contains(result, n => n.FullPath == folder2);
        Assert.Contains(result, n => n.FullPath == folder3);
    }

    [Fact]
    public void GetTopLevelItems_MixedParentsAndSeparateFolders_ReturnsCorrectItems()
    {
        var parent = MakePath("", "parent");
        var child = MakePath("", "parent", "child");
        var separate = MakePath("", "separate");
        var another = MakePath("", "another");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = parent },
            new TreeNode { FullPath = child },
            new TreeNode { FullPath = separate },
            new TreeNode { FullPath = another }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Equal(3, result.Count);
        Assert.Contains(result, n => n.FullPath == parent);
        Assert.Contains(result, n => n.FullPath == separate);
        Assert.Contains(result, n => n.FullPath == another);
        Assert.DoesNotContain(result, n => n.FullPath == child);
    }

    [Fact]
    public void GetTopLevelItems_PathsWithTrailingSeparators_HandlesCorrectly()
    {
        var parent = MakePath("", "parent") + Path.DirectorySeparatorChar;
        var child = MakePath("", "parent", "child");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = parent },
            new TreeNode { FullPath = child }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Single(result);
        Assert.Equal(parent, result[0].FullPath);
    }

    [Fact]
    public void GetTopLevelItems_CaseInsensitiveComparison_FiltersCorrectly()
    {
        // The implementation uses OrdinalIgnoreCase, so it's case-insensitive on all platforms
        var parent = MakePath("", "PARENT");
        var child = MakePath("", "parent", "child");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = parent },
            new TreeNode { FullPath = child }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        // Should filter out child since /parent is a parent of /parent/child (case-insensitive)
        Assert.Single(result);
    }

    [Fact]
    public void GetTopLevelItems_SimilarPathNames_OnlyFiltersActualChildren()
    {
        // "/test" should not filter out "/testing" even though one starts with the other
        var test = MakePath("", "test");
        var testing = MakePath("", "testing");
        
        var items = new List<TreeNode>
        {
            new TreeNode { FullPath = test },
            new TreeNode { FullPath = testing }
        };

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetTopLevelItems_DeepHierarchy_PerformanceCheck()
    {
        // Create a large list with deep nesting
        var items = new List<TreeNode>();
        var root = MakePath("", "root");
        
        for (int i = 0; i < 100; i++)
        {
            var level = MakePath("", "root", $"level{i}");
            var sublevel = MakePath("", "root", $"level{i}", "sublevel");
            
            items.Add(new TreeNode { FullPath = level });
            items.Add(new TreeNode { FullPath = sublevel });
        }

        var result = TreeNodeHelper.GetTopLevelItems(items);
        
        // Should filter out all sublevel items
        Assert.Equal(100, result.Count);
        Assert.All(result, node => Assert.DoesNotContain("sublevel", node.FullPath.ToLowerInvariant()));
    }
}
