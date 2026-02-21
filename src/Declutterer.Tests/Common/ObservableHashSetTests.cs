using Declutterer.Utilities.Helpers;

namespace Declutterer.Tests.Common;

public class ObservableHashSetTests
{
    [Fact]
    public void Add_UniqueItem_AddsSuccessfully()
    {
        var set = new ObservableHashSet<int>();
        set.Add(1);
        
        Assert.Single(set);
        Assert.Contains(1, set);
    }

    [Fact]
    public void Add_DuplicateItem_DoesNotAddAgain()
    {
        var set = new ObservableHashSet<int>();
        set.Add(1);
        set.Add(1);
        
        Assert.Single(set);
    }

    [Fact]
    public void Add_MultipleUniqueItems_AddsAll()
    {
        var set = new ObservableHashSet<string>();
        set.Add("apple");
        set.Add("banana");
        set.Add("cherry");
        
        Assert.Equal(3, set.Count);
        Assert.Contains("apple", set);
        Assert.Contains("banana", set);
        Assert.Contains("cherry", set);
    }

    [Fact]
    public void Add_MultipleDuplicates_OnlyAddsOnce()
    {
        var set = new ObservableHashSet<string>();
        set.Add("test");
        set.Add("test");
        set.Add("test");
        
        Assert.Single(set);
        Assert.Equal("test", set[0]);
    }

    [Fact]
    public void Insert_UniqueItem_InsertsSuccessfully()
    {
        var set = new ObservableHashSet<int>();
        set.Insert(0, 10);
        
        Assert.Single(set);
        Assert.Equal(10, set[0]);
    }

    [Fact]
    public void Insert_DuplicateItem_DoesNotInsert()
    {
        var set = new ObservableHashSet<int>();
        set.Add(10);
        set.Insert(0, 10); // Try to insert duplicate
        
        Assert.Single(set);
    }

    [Fact]
    public void Indexer_Set_WithUniqueItem_UpdatesSuccessfully()
    {
        var set = new ObservableHashSet<string>();
        set.Add("old");
        set[0] = "new";
        
        Assert.Single(set);
        Assert.Equal("new", set[0]);
    }

    [Fact]
    public void Indexer_Set_WithDuplicateItem_DoesNotUpdate()
    {
        var set = new ObservableHashSet<string>();
        set.Add("first");
        set.Add("second");
        
        set[1] = "first"; // Try to set to duplicate
        
        Assert.Equal(2, set.Count);
        Assert.Equal("second", set[1]); // Should remain unchanged
    }

    [Fact]
    public void Remove_ExistingItem_RemovesSuccessfully()
    {
        var set = new ObservableHashSet<int>();
        set.Add(1);
        set.Add(2);
        
        var removed = set.Remove(1);
        
        Assert.True(removed);
        Assert.Single(set);
        Assert.DoesNotContain(1, set);
    }

    [Fact]
    public void Clear_WithItems_RemovesAll()
    {
        var set = new ObservableHashSet<int>();
        set.Add(1);
        set.Add(2);
        set.Add(3);
        
        set.Clear();
        
        Assert.Empty(set);
    }

    [Fact]
    public void Contains_ExistingItem_ReturnsTrue()
    {
        var set = new ObservableHashSet<string>();
        set.Add("test");
        
        Assert.Contains("test", set);
    }

    [Fact]
    public void Contains_NonExistingItem_ReturnsFalse()
    {
        var set = new ObservableHashSet<string>();
        set.Add("test");
        
        Assert.DoesNotContain("other", set);
    }

    [Fact]
    public void Count_AfterMultipleOperations_ReturnsCorrectCount()
    {
        var set = new ObservableHashSet<int>();
        
        Assert.Empty(set);
        
        set.Add(1);
        Assert.Single(set);
        
        set.Add(2);
        Assert.Equal(2, set.Count);
        
        set.Add(1); // Duplicate
        Assert.Equal(2, set.Count);
        
        set.Remove(1);
        Assert.Single(set);
    }

    [Fact]
    public void ObservableHashSet_WithReferenceTypes_WorksCorrectly()
    {
        var obj1 = new TestClass { Id = 1, Name = "Test1" };
        var obj2 = new TestClass { Id = 2, Name = "Test2" };
        
        var set = new ObservableHashSet<TestClass>();
        set.Add(obj1);
        set.Add(obj2);
        set.Add(obj1); // Try to add duplicate reference
        
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void AddRange_WithMixedDuplicates_OnlyAddsUnique()
    {
        var set = new ObservableHashSet<int>();
        set.Add(1);
        set.Add(2);
        
        // Add more items, some duplicates
        set.Add(2); // Duplicate
        set.Add(3); // New
        set.Add(1); // Duplicate
        set.Add(4); // New
        
        Assert.Equal(4, set.Count);
        Assert.Contains(1, set);
        Assert.Contains(2, set);
        Assert.Contains(3, set);
        Assert.Contains(4, set);
    }

    private class TestClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
