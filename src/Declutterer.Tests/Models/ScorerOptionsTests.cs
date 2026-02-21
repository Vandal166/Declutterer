using Declutterer.Domain.Models;

namespace Declutterer.Tests.Models;

public class ScorerOptionsTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var options = new ScorerOptions();
        
        Assert.Equal(0.5, options.WeightAge);
        Assert.Equal(0.5, options.WeightSize);
        Assert.Equal(0.4, options.TopPercentage);
    }

    [Fact]
    public void WeightAge_CanBeSetAndRetrieved()
    {
        var options = new ScorerOptions();
        
        options.WeightAge = 0.7;
        
        Assert.Equal(0.7, options.WeightAge);
    }

    [Fact]
    public void WeightSize_CanBeSetAndRetrieved()
    {
        var options = new ScorerOptions();
        
        options.WeightSize = 0.3;
        
        Assert.Equal(0.3, options.WeightSize);
    }

    [Fact]
    public void TopPercentage_CanBeSetAndRetrieved()
    {
        var options = new ScorerOptions();
        
        options.TopPercentage = 0.25;
        
        Assert.Equal(0.25, options.TopPercentage);
    }

    [Fact]
    public void Weights_CanBeSetToZero()
    {
        var options = new ScorerOptions();
        
        options.WeightAge = 0.0;
        options.WeightSize = 0.0;
        
        Assert.Equal(0.0, options.WeightAge);
        Assert.Equal(0.0, options.WeightSize);
    }

    [Fact]
    public void Weights_CanBeSetToOne()
    {
        var options = new ScorerOptions();
        
        options.WeightAge = 1.0;
        options.WeightSize = 1.0;
        
        Assert.Equal(1.0, options.WeightAge);
        Assert.Equal(1.0, options.WeightSize);
    }

    [Fact]
    public void TopPercentage_CanBeSetToFullRange()
    {
        var options = new ScorerOptions();
        
        options.TopPercentage = 0.0;
        Assert.Equal(0.0, options.TopPercentage);
        
        options.TopPercentage = 1.0;
        Assert.Equal(1.0, options.TopPercentage);
    }

    [Theory]
    [InlineData(0.3, 0.7, 0.5)]
    [InlineData(0.6, 0.4, 0.3)]
    [InlineData(1.0, 0.0, 0.2)]
    [InlineData(0.0, 1.0, 0.8)]
    public void AllProperties_CanBeSetToVariousValues(double weightAge, double weightSize, double topPercentage)
    {
        var options = new ScorerOptions
        {
            WeightAge = weightAge,
            WeightSize = weightSize,
            TopPercentage = topPercentage
        };
        
        Assert.Equal(weightAge, options.WeightAge);
        Assert.Equal(weightSize, options.WeightSize);
        Assert.Equal(topPercentage, options.TopPercentage);
    }
}
