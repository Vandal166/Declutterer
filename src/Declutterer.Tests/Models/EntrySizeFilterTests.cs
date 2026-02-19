using Declutterer.Models;

namespace Declutterer.Tests.Models;

public class EntrySizeFilterTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var filter = new EntrySizeFilter();
        
        Assert.Equal(1, filter.SizeThreshold);
        Assert.False(filter.UseSizeFilter);
    }

    [Fact]
    public void SizeThreshold_WhenSetToPositive_EnablesUseSizeFilter()
    {
        var filter = new EntrySizeFilter();
        
        filter.SizeThreshold = 100;
        
        Assert.True(filter.UseSizeFilter);
        Assert.Equal(100, filter.SizeThreshold);
    }

    [Fact]
    public void SizeThreshold_WhenSetToZero_DoesNotEnableFilter()
    {
        var filter = new EntrySizeFilter();
        filter.UseSizeFilter = false;
        
        filter.SizeThreshold = 0;
        
        Assert.False(filter.UseSizeFilter);
    }

    [Fact]
    public void SizeThreshold_WhenSetToNegative_DoesNotEnableFilter()
    {
        var filter = new EntrySizeFilter();
        filter.UseSizeFilter = false;
        
        filter.SizeThreshold = -10;
        
        Assert.False(filter.UseSizeFilter);
    }

    [Fact]
    public void UseSizeFilter_CanBeToggledManually()
    {
        var filter = new EntrySizeFilter();
        
        filter.UseSizeFilter = true;
        Assert.True(filter.UseSizeFilter);
        
        filter.UseSizeFilter = false;
        Assert.False(filter.UseSizeFilter);
    }

    [Fact]
    public void PropertyChanged_RaisedOnSizeThresholdChange()
    {
        var filter = new EntrySizeFilter();
        var propertyChangedRaised = false;
        
        filter.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(EntrySizeFilter.SizeThreshold))
            {
                propertyChangedRaised = true;
            }
        };
        
        filter.SizeThreshold = 50;
        
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void PropertyChanged_RaisedOnUseSizeFilterChange()
    {
        var filter = new EntrySizeFilter();
        var propertyChangedRaised = false;
        
        filter.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(EntrySizeFilter.UseSizeFilter))
            {
                propertyChangedRaised = true;
            }
        };
        
        filter.UseSizeFilter = true;
        
        Assert.True(propertyChangedRaised);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void SizeThreshold_VariousPositiveValues_EnablesFilter(long size)
    {
        var filter = new EntrySizeFilter();
        filter.SizeThreshold = 0; // Reset to 0 first
        filter.UseSizeFilter = false;
        
        filter.SizeThreshold = size;
        
        Assert.True(filter.UseSizeFilter);
        Assert.Equal(size, filter.SizeThreshold);
    }
}