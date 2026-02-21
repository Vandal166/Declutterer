using System.Globalization;
using Avalonia.Media;
using Declutterer.UI.Converters;

namespace Declutterer.Tests.Converters;

public class BoolToFontWeightConverterTests
{
    private readonly BoolToFontWeightConverter _converter = new();

    [Fact]
    public void Convert_TrueValue_ReturnsBold()
    {
        var result = _converter.Convert(true, typeof(FontWeight), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(FontWeight.Bold, result);
    }

    [Fact]
    public void Convert_FalseValue_ReturnsNormal()
    {
        var result = _converter.Convert(false, typeof(FontWeight), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(FontWeight.Normal, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsNormal()
    {
        var result = _converter.Convert(null, typeof(FontWeight), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(FontWeight.Normal, result);
    }

    [Fact]
    public void Convert_NonBooleanValue_ReturnsNormal()
    {
        var result = _converter.Convert("not a boolean", typeof(FontWeight), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(FontWeight.Normal, result);
    }

    [Fact]
    public void Convert_IntegerValue_ReturnsNormal()
    {
        var result = _converter.Convert(1, typeof(FontWeight), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(FontWeight.Normal, result);
    }

    [Fact]
    public void Convert_StringValue_ReturnsNormal()
    {
        var result = _converter.Convert("true", typeof(FontWeight), null, CultureInfo.InvariantCulture);
        
        Assert.NotNull(result);
        Assert.Equal(FontWeight.Normal, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(FontWeight.Bold, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
