using System.Globalization;
using Declutterer.UI.Converters;

namespace Declutterer.Tests.Converters;

public class DepthToIndentConverterTests
{
    private readonly DepthToIndentConverter _converter = new();
    private const int IndentPerLevel = 20;

    [Fact]
    public void Convert_ZeroDepth_ReturnsZero()
    {
        var result = _converter.Convert(0, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void Convert_Depth1_Returns20()
    {
        var result = _converter.Convert(1, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(IndentPerLevel, result);
    }

    [Fact]
    public void Convert_Depth2_Returns40()
    {
        var result = _converter.Convert(2, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(IndentPerLevel * 2, result);
    }

    [Fact]
    public void Convert_Depth3_Returns60()
    {
        var result = _converter.Convert(3, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(IndentPerLevel * 3, result);
    }

    [Fact]
    public void Convert_Depth5_Returns100()
    {
        var result = _converter.Convert(5, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(IndentPerLevel * 5, result);
    }

    [Fact]
    public void Convert_Depth10_Returns200()
    {
        var result = _converter.Convert(10, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(IndentPerLevel * 10, result);
    }

    [Fact]
    public void Convert_LargeDepth_ReturnsCorrectValue()
    {
        var result = _converter.Convert(100, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(IndentPerLevel * 100, result);
    }

    [Fact]
    public void Convert_NegativeDepth_ReturnsNegativeValue()
    {
        var result = _converter.Convert(-1, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(-IndentPerLevel, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsZero()
    {
        var result = _converter.Convert(null, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void Convert_NonIntValue_ReturnsZero()
    {
        var result = _converter.Convert("not an int", typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void Convert_DoubleValue_ReturnsZero()
    {
        var result = _converter.Convert(5.5, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void Convert_BooleanValue_ReturnsZero()
    {
        var result = _converter.Convert(true, typeof(int), null, CultureInfo.InvariantCulture);
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(20, typeof(int), null, CultureInfo.InvariantCulture));
    }
}
