using Declutterer.Domain.Services.Cleanup.Rules;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Cleanup;

public sealed class FilePatternRuleTests
{
    [Fact]
    public void FilesProbablyIdentical_DetectsDifferencesOutsideSampledRegions()
    {
        using var temp = new TempTestDirectory();
        var firstPath = Path.Combine(temp.Path, "first.bin");
        var secondPath = Path.Combine(temp.Path, "second.bin");
        var first = new byte[256 * 1024];
        new Random(123).NextBytes(first);
        var second = (byte[])first.Clone();
        second[16 * 1024] ^= 0xFF;
        File.WriteAllBytes(firstPath, first);
        File.WriteAllBytes(secondPath, second);

        Assert.False(FilePatternRule.FilesProbablyIdentical(firstPath, secondPath));
    }

    [Fact]
    public void FilesProbablyIdentical_ReturnsTrueForMatchingLargeFiles()
    {
        using var temp = new TempTestDirectory();
        var firstPath = Path.Combine(temp.Path, "first.bin");
        var secondPath = Path.Combine(temp.Path, "second.bin");
        var content = new byte[256 * 1024];
        new Random(123).NextBytes(content);
        File.WriteAllBytes(firstPath, content);
        File.WriteAllBytes(secondPath, content);

        Assert.True(FilePatternRule.FilesProbablyIdentical(firstPath, secondPath));
    }
}
