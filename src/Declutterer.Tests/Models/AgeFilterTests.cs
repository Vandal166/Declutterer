using Declutterer.Domain.Models;

namespace Declutterer.Tests.Models;

public class AgeFilterTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var filter = new AgeFilter();
        
        Assert.Equal(1, filter.MonthsModifiedValue);
        Assert.Equal(1, filter.MonthsAccessedValue);
        Assert.Null(filter.ModifiedBefore);
        Assert.Null(filter.AccessedBefore);
        Assert.False(filter.UseModifiedDate);
        Assert.False(filter.UseAccessedDate);
    }

    [Fact]
    public void MonthsModifiedValue_WhenSetToPositive_EnablesUseModifiedDate()
    {
        var filter = new AgeFilter();
        
        filter.MonthsModifiedValue = 6;
        
        Assert.True(filter.UseModifiedDate);
        Assert.Equal(6, filter.MonthsModifiedValue);
    }

    [Fact]
    public void MonthsModifiedValue_WhenSetToZero_DoesNotEnableFilter()
    {
        var filter = new AgeFilter();
        filter.UseModifiedDate = false;
        
        filter.MonthsModifiedValue = 0;
        
        Assert.False(filter.UseModifiedDate);
    }

    [Fact]
    public void MonthsAccessedValue_WhenSetToPositive_EnablesUseAccessedDate()
    {
        var filter = new AgeFilter();
        
        filter.MonthsAccessedValue = 3;
        
        Assert.True(filter.UseAccessedDate);
        Assert.Equal(3, filter.MonthsAccessedValue);
    }

    [Fact]
    public void MonthsAccessedValue_WhenSetToZero_DoesNotEnableFilter()
    {
        var filter = new AgeFilter();
        filter.UseAccessedDate = false;
        
        filter.MonthsAccessedValue = 0;
        
        Assert.False(filter.UseAccessedDate);
    }

    [Fact]
    public void ModifiedBefore_CanBeSetAndRetrieved()
    {
        var filter = new AgeFilter();
        var date = new DateTime(2024, 1, 1);
        
        filter.ModifiedBefore = date;
        
        Assert.Equal(date, filter.ModifiedBefore);
    }

    [Fact]
    public void AccessedBefore_CanBeSetAndRetrieved()
    {
        var filter = new AgeFilter();
        var date = new DateTime(2024, 1, 1);
        
        filter.AccessedBefore = date;
        
        Assert.Equal(date, filter.AccessedBefore);
    }

    [Fact]
    public void UseModifiedDate_CanBeToggledManually()
    {
        var filter = new AgeFilter();
        
        filter.UseModifiedDate = true;
        Assert.True(filter.UseModifiedDate);
        
        filter.UseModifiedDate = false;
        Assert.False(filter.UseModifiedDate);
    }

    [Fact]
    public void UseAccessedDate_CanBeToggledManually()
    {
        var filter = new AgeFilter();
        
        filter.UseAccessedDate = true;
        Assert.True(filter.UseAccessedDate);
        
        filter.UseAccessedDate = false;
        Assert.False(filter.UseAccessedDate);
    }

    [Fact]
    public void PropertyChanged_RaisedOnMonthsModifiedValueChange()
    {
        var filter = new AgeFilter();
        var propertyChangedRaised = false;
        
        filter.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(AgeFilter.MonthsModifiedValue))
            {
                propertyChangedRaised = true;
            }
        };
        
        filter.MonthsModifiedValue = 12;
        
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void PropertyChanged_RaisedOnMonthsAccessedValueChange()
    {
        var filter = new AgeFilter();
        var propertyChangedRaised = false;
        
        filter.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(AgeFilter.MonthsAccessedValue))
            {
                propertyChangedRaised = true;
            }
        };
        
        filter.MonthsAccessedValue = 6;
        
        Assert.True(propertyChangedRaised);
    }
}
