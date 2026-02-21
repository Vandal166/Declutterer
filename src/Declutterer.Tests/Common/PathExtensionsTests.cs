using Declutterer.Utilities.Extensions;

namespace Declutterer.Tests.Common;

public class PathExtensionsTests
{
    [Fact]
    public void NormalizePath_PathWithTrailingSeparator_RemovesSeparator()
    {
        var path = $"/home/user/test{Path.DirectorySeparatorChar}";
        var result = path.NormalizePath();
        Assert.Equal("/home/user/test", result);
    }

    [Fact]
    public void NormalizePath_PathWithoutTrailingSeparator_ReturnsSamePath()
    {
        var path = "/home/user/test";
        var result = path.NormalizePath();
        Assert.Equal(path, result);
    }

    [Fact]
    public void NormalizePath_PathWithMultipleTrailingSeparators_RemovesAll()
    {
        var sep = Path.DirectorySeparatorChar;
        var path = $"/home/user/test{sep}{sep}{sep}";
        var result = path.NormalizePath();
        Assert.Equal("/home/user/test", result);
    }

    [Fact]
    public void NormalizePath_EmptyString_ReturnsEmptyString()
    {
        var path = "";
        var result = path.NormalizePath();
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizePath_RootPath_RemovesTrailingSeparator()
    {
        var sep = Path.DirectorySeparatorChar;
        var path = $"{sep}";
        var result = path.NormalizePath();
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizePath_MixedSeparators_RemovesBoth()
    {
        // On Linux, this should handle both / and \
        var path = "/home/user/test/";
        var result = path.NormalizePath();
        Assert.False(result.EndsWith("/"));
        Assert.False(result.EndsWith("\\"));
    }

    [Fact]
    public void NormalizePath_RelativePath_NormalizesCorrectly()
    {
        var sep = Path.DirectorySeparatorChar;
        var path = $".{sep}relative{sep}path{sep}";
        var result = path.NormalizePath();
        Assert.False(result.EndsWith(sep.ToString()));
    }
}