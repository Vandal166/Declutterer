using Declutterer.Models;

namespace Declutterer.Tests.Models;

public class TreeNodeTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var node = new TreeNode();
        
        Assert.Equal(string.Empty, node.Name);
        Assert.Equal(string.Empty, node.FullPath);
        Assert.Equal(0, node.Size);
        Assert.Null(node.LastModified);
        Assert.Null(node.LastAccessed);
        Assert.False(node.IsDirectory);
        Assert.False(node.IsCheckboxSelected);
        Assert.True(node.IsCheckboxEnabled);
        Assert.False(node.IsExpanded);
        Assert.Equal(0, node.Depth);
        Assert.False(node.HasChildren);
        Assert.Null(node.Parent);
        Assert.NotNull(node.Children);
        Assert.Empty(node.Children);
        Assert.Null(node.Icon);
    }

    [Fact]
    public void Name_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        
        node.Name = "TestFile.txt";
        
        Assert.Equal("TestFile.txt", node.Name);
    }

    [Fact]
    public void FullPath_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        var path = "/home/user/test.txt";
        
        node.FullPath = path;
        
        Assert.Equal(path, node.FullPath);
    }

    [Fact]
    public void Size_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        
        node.Size = 1024 * 1024; // 1 MB
        
        Assert.Equal(1024 * 1024, node.Size);
    }

    [Fact]
    public void SizeFormatted_ReturnsFormattedString()
    {
        var node = new TreeNode();
        
        node.Size = 1024 * 1024; // 1 MB
        
        Assert.Equal("1 MB", node.SizeFormatted);
    }

    [Fact]
    public void IsSizeBold_WhenSizeLessThan1GB_ReturnsFalse()
    {
        var node = new TreeNode();
        
        node.Size = 1024 * 1024 * 512; // 512 MB
        
        Assert.False(node.IsSizeBold);
    }

    [Fact]
    public void IsSizeBold_WhenSizeEquals1GB_ReturnsTrue()
    {
        var node = new TreeNode();
        
        node.Size = 1024L * 1024 * 1024; // 1 GB
        
        Assert.True(node.IsSizeBold);
    }

    [Fact]
    public void IsSizeBold_WhenSizeGreaterThan1GB_ReturnsTrue()
    {
        var node = new TreeNode();
        
        node.Size = 2L * 1024 * 1024 * 1024; // 2 GB
        
        Assert.True(node.IsSizeBold);
    }

    [Fact]
    public void LastModified_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        var date = new DateTime(2024, 1, 1);
        
        node.LastModified = date;
        
        Assert.Equal(date, node.LastModified);
    }

    [Fact]
    public void LastAccessed_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        var date = new DateTime(2024, 6, 15);
        
        node.LastAccessed = date;
        
        Assert.Equal(date, node.LastAccessed);
    }

    [Fact]
    public void IsDirectory_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        
        node.IsDirectory = true;
        
        Assert.True(node.IsDirectory);
    }

    [Fact]
    public void IsCheckboxSelected_CanBeToggled()
    {
        var node = new TreeNode();
        
        node.IsCheckboxSelected = true;
        Assert.True(node.IsCheckboxSelected);
        
        node.IsCheckboxSelected = false;
        Assert.False(node.IsCheckboxSelected);
    }

    [Fact]
    public void IsCheckboxEnabled_CanBeToggled()
    {
        var node = new TreeNode();
        
        node.IsCheckboxEnabled = false;
        Assert.False(node.IsCheckboxEnabled);
        
        node.IsCheckboxEnabled = true;
        Assert.True(node.IsCheckboxEnabled);
    }

    [Fact]
    public void IsExpanded_CanBeToggled()
    {
        var node = new TreeNode();
        
        node.IsExpanded = true;
        Assert.True(node.IsExpanded);
        
        node.IsExpanded = false;
        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void Depth_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        
        node.Depth = 3;
        
        Assert.Equal(3, node.Depth);
    }

    [Fact]
    public void HasChildren_CanBeSetAndRetrieved()
    {
        var node = new TreeNode();
        
        node.HasChildren = true;
        
        Assert.True(node.HasChildren);
    }

    [Fact]
    public void Parent_CanBeSetAndRetrieved()
    {
        var parent = new TreeNode { Name = "Parent" };
        var child = new TreeNode { Name = "Child" };
        
        child.Parent = parent;
        
        Assert.Equal(parent, child.Parent);
    }

    [Fact]
    public void Children_CanAddAndRemoveItems()
    {
        var parent = new TreeNode();
        var child1 = new TreeNode { Name = "Child1" };
        var child2 = new TreeNode { Name = "Child2" };
        
        parent.Children.Add(child1);
        parent.Children.Add(child2);
        
        Assert.Equal(2, parent.Children.Count);
        Assert.Contains(child1, parent.Children);
        Assert.Contains(child2, parent.Children);
        
        parent.Children.Remove(child1);
        
        Assert.Single(parent.Children);
        Assert.DoesNotContain(child1, parent.Children);
    }

    [Fact]
    public void PropertyChanged_RaisedOnNameChange()
    {
        var node = new TreeNode();
        var propertyChangedRaised = false;
        
        node.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TreeNode.Name))
            {
                propertyChangedRaised = true;
            }
        };
        
        node.Name = "NewName";
        
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void PropertyChanged_RaisedOnSizeChange()
    {
        var node = new TreeNode();
        var propertyChangedRaised = false;
        
        node.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TreeNode.Size))
            {
                propertyChangedRaised = true;
            }
        };
        
        node.Size = 1024;
        
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void TreeHierarchy_CanBeBuilt()
    {
        var root = new TreeNode { Name = "Root", Depth = 0 };
        var child1 = new TreeNode { Name = "Child1", Depth = 1, Parent = root };
        var child2 = new TreeNode { Name = "Child2", Depth = 1, Parent = root };
        var grandchild = new TreeNode { Name = "Grandchild", Depth = 2, Parent = child1 };
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild);
        
        Assert.Equal(2, root.Children.Count);
        Assert.Single(child1.Children);
        Assert.Empty(child2.Children);
        Assert.Equal(root, child1.Parent);
        Assert.Equal(child1, grandchild.Parent);
    }
}
