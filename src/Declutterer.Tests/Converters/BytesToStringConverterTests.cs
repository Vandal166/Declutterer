using System.Globalization;
using Declutterer.UI.Converters;

namespace Declutterer.Tests.Converters;

public class BytesToStringConverterTests
{
    private readonly BytesToStringConverter _converter = new();

    [Fact]
    public void Convert_ZeroBytes_Returns0B()
    {
        var result = _converter.Convert(0L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void Convert_Bytes_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(512L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("512 B", result);
    }

    [Fact]
    public void Convert_1023Bytes_ReturnsBytes()
    {
        var result = _converter.Convert(1023L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1023 B", result);
    }

    [Fact]
    public void Convert_1KB_ReturnsKB()
    {
        var result = _converter.Convert(1024L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1 KB", result);
    }

    [Fact]
    public void Convert_1536Bytes_Returns1Point5KB()
    {
        var result = _converter.Convert(1536L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1.5 KB", result);
    }

    [Fact]
    public void Convert_1MB_ReturnsMB()
    {
        var result = _converter.Convert(1024L * 1024, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1 MB", result);
    }

    [Fact]
    public void Convert_2Point5MB_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(2621440L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("2.5 MB", result);
    }

    [Fact]
    public void Convert_1GB_ReturnsGB()
    {
        var result = _converter.Convert(1024L * 1024 * 1024, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1 GB", result);
    }

    [Fact]
    public void Convert_1Point5GB_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(1610612736L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1.5 GB", result);
    }

    [Fact]
    public void Convert_1TB_ReturnsTB()
    {
        var result = _converter.Convert(1024L * 1024 * 1024 * 1024, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1 TB", result);
    }

    [Fact]
    public void Convert_LargeValue_ReturnsTB()
    {
        var result = _converter.Convert(5L * 1024 * 1024 * 1024 * 1024, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("5 TB", result);
    }

    [Fact]
    public void Convert_DecimalPrecision_RoundsCorrectly()
    {
        var result = _converter.Convert(1536000L, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("1.46 MB", result);
    }

    [Fact]
    public void Convert_NullValue_Returns0B()
    {
        var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void Convert_NonLongValue_Returns0B()
    {
        var result = _converter.Convert("not a long", typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void Convert_IntValue_Returns0B()
    {
        var result = _converter.Convert(1024, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void Convert_DoubleValue_Returns0B()
    {
        var result = _converter.Convert(1024.0, typeof(string), null, CultureInfo.InvariantCulture);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("1 KB", typeof(long), null, CultureInfo.InvariantCulture));
    }
}
