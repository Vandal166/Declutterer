using System.Globalization;
using Declutterer.Converters;
using Material.Icons;

namespace Declutterer.Tests.Converters;

public class ExpanderIconConverterTests
{
    private readonly ExpanderIconConverter _converter = new();

    [Fact]
    public void Convert_TrueValue_ReturnsChevronDown()
    {
        var result = _converter.Convert(true, typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronDown, result);
    }

    [Fact]
    public void Convert_FalseValue_ReturnsChevronRight()
    {
        var result = _converter.Convert(false, typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronRight, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsChevronRight()
    {
        var result = _converter.Convert(null, typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronRight, result);
    }

    [Fact]
    public void Convert_NonBooleanValue_ReturnsChevronRight()
    {
        var result = _converter.Convert("not a boolean", typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronRight, result);
    }

    [Fact]
    public void Convert_IntegerValue_ReturnsChevronRight()
    {
        var result = _converter.Convert(1, typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronRight, result);
    }

    [Fact]
    public void Convert_StringValue_ReturnsChevronRight()
    {
        var result = _converter.Convert("true", typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronRight, result);
    }

    [Fact]
    public void Convert_DoubleValue_ReturnsChevronRight()
    {
        var result = _converter.Convert(1.5, typeof(MaterialIconKind), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(MaterialIconKind.ChevronRight, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(MaterialIconKind.ChevronDown, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
