using System.Globalization;
using Avalonia.Media;
using Declutterer.Converters;

namespace Declutterer.Tests.Converters;

public class SizeToBrushConverterTests
{
    private readonly SizeToBrushConverter _converter = new();

    [Fact]
    public void Convert_ZeroBytes_ReturnsGrayBrush()
    {
        var result = _converter.Convert(0L, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#808080"), brush.Color);
    }

    [Fact]
    public void Convert_500KB_ReturnsGrayBrush()
    {
        var result = _converter.Convert(500L * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#808080"), brush.Color);
    }

    [Fact]
    public void Convert_JustUnder1MB_ReturnsGrayBrush()
    {
        var result = _converter.Convert(1024L * 1024 - 1, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#808080"), brush.Color);
    }

    [Fact]
    public void Convert_Exactly1MB_ReturnsBlackBrush()
    {
        var result = _converter.Convert(1024L * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_100MB_ReturnsBlackBrush()
    {
        var result = _converter.Convert(100L * 1024 * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_JustUnder250MB_ReturnsBlackBrush()
    {
        var result = _converter.Convert(250L * 1024 * 1024 - 1, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_Exactly250MB_ReturnsDarkOrangeBrush()
    {
        var result = _converter.Convert(250L * 1024 * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#FF8C00"), brush.Color);
    }

    [Fact]
    public void Convert_500MB_ReturnsDarkOrangeBrush()
    {
        var result = _converter.Convert(500L * 1024 * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#FF8C00"), brush.Color);
    }

    [Fact]
    public void Convert_JustUnder1GB_ReturnsDarkOrangeBrush()
    {
        var result = _converter.Convert(1024L * 1024 * 1024 - 1, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#FF8C00"), brush.Color);
    }

    [Fact]
    public void Convert_Exactly1GB_ReturnsCrimsonBrush()
    {
        var result = _converter.Convert(1024L * 1024 * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#DC143C"), brush.Color);
    }

    [Fact]
    public void Convert_5GB_ReturnsCrimsonBrush()
    {
        var result = _converter.Convert(5L * 1024 * 1024 * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#DC143C"), brush.Color);
    }

    [Fact]
    public void Convert_1TB_ReturnsCrimsonBrush()
    {
        var result = _converter.Convert(1024L * 1024 * 1024 * 1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#DC143C"), brush.Color);
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
    public void Convert_NonLongValue_ReturnsBlackBrush()
    {
        var result = _converter.Convert("not a long", typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Convert_IntValue_ReturnsBlackBrush()
    {
        var result = _converter.Convert(1024, typeof(IBrush), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(new SolidColorBrush(Colors.Black), typeof(long), null, CultureInfo.InvariantCulture));
    }
}
