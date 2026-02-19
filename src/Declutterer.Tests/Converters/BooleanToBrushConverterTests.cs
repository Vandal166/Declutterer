using System.Globalization;
using Avalonia.Media;
using Declutterer.Converters;

namespace Declutterer.Tests.Converters;

public class BooleanToBrushConverterTests
{
    private readonly BooleanToBrushConverter _converter = new();

    [Fact]
    public void Convert_TrueValue_ReturnsGrayBrush()
    {
        var result = _converter.Convert(true, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Gray, brush.Color);
    }

    [Fact]
    public void Convert_FalseValue_ReturnsBlackBrush()
    {
        var result = _converter.Convert(false, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_NullValue_ReturnsBlackBrush()
    {
        var result = _converter.Convert(null, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_NonBooleanValue_ReturnsBlackBrush()
    {
        var result = _converter.Convert("not a boolean", typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_IntegerValue_ReturnsBlackBrush()
    {
        var result = _converter.Convert(42, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(new SolidColorBrush(Colors.Gray), typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
