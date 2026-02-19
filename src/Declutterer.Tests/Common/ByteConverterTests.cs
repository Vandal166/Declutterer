using Declutterer.Common;

namespace Declutterer.Tests.Common;

public class ByteConverterTests
{
    [Fact]
    public void ToReadableString_ZeroBytes_ReturnsZeroB()
    {
        var result = ByteConverter.ToReadableString(0);
        Assert.Equal("0B", result);
    }

    [Fact]
    public void ToReadableString_SmallBytes_ReturnsBytes()
    {
        var result = ByteConverter.ToReadableString(512);
        Assert.Equal("512 B", result);
    }

    [Fact]
    public void ToReadableString_Kilobytes_ReturnsKB()
    {
        var result = ByteConverter.ToReadableString(1024);
        Assert.Equal("1 KB", result);
    }

    [Fact]
    public void ToReadableString_Megabytes_ReturnsMB()
    {
        var result = ByteConverter.ToReadableString(1024 * 1024);
        Assert.Equal("1 MB", result);
    }

    [Fact]
    public void ToReadableString_Gigabytes_ReturnsGB()
    {
        var result = ByteConverter.ToReadableString(1024L * 1024 * 1024);
        Assert.Equal("1 GB", result);
    }

    [Fact]
    public void ToReadableString_Terabytes_ReturnsTB()
    {
        var result = ByteConverter.ToReadableString(1024L * 1024 * 1024 * 1024);
        Assert.Equal("1 TB", result);
    }

    [Fact]
    public void ToReadableString_DecimalValue_RoundsToOneDecimal()
    {
        var result = ByteConverter.ToReadableString(1536); // 1.5 KB
        Assert.Equal("1.5 KB", result);
    }

    [Fact]
    public void ToReadableString_NegativeValue_PreservesSign()
    {
        var result = ByteConverter.ToReadableString(-1024);
        Assert.Equal("-1 KB", result);
    }

    [Fact]
    public void ToReadableTuple_ZeroBytes_ReturnsTupleWithZeroAndB()
    {
        var (value, suffix) = ByteConverter.ToReadableTuple(0);
        Assert.Equal(0, value);
        Assert.Equal("B", suffix);
    }

    [Fact]
    public void ToReadableTuple_Kilobytes_ReturnsTupleWithCorrectValues()
    {
        var (value, suffix) = ByteConverter.ToReadableTuple(1024);
        Assert.Equal(1, value);
        Assert.Equal("KB", suffix);
    }

    [Fact]
    public void ToReadableTuple_Megabytes_ReturnsTupleWithCorrectValues()
    {
        var (value, suffix) = ByteConverter.ToReadableTuple(1024 * 1024);
        Assert.Equal(1, value);
        Assert.Equal("MB", suffix);
    }

    [Fact]
    public void ToReadableTuple_DecimalValue_ReturnsRoundedValue()
    {
        var (value, suffix) = ByteConverter.ToReadableTuple(1536); // 1.5 KB
        Assert.Equal(2, value); // Rounds to 2
        Assert.Equal("KB", suffix);
    }

    [Fact]
    public void ToReadableTuple_NegativeValue_PreservesSign()
    {
        var (value, suffix) = ByteConverter.ToReadableTuple(-2048);
        Assert.Equal(-2, value);
        Assert.Equal("KB", suffix);
    }

    [Theory]
    [InlineData(100, "100 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5242880, "5 MB")]
    [InlineData(1073741824L, "1 GB")]
    public void ToReadableString_VariousValues_ReturnsExpectedFormat(long bytes, string expected)
    {
        var result = ByteConverter.ToReadableString(bytes);
        Assert.Equal(expected, result);
    }
}
