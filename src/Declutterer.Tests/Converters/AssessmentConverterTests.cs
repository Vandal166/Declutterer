using System.Globalization;
using Avalonia.Media;
using Declutterer.Domain.Models;
using Declutterer.UI.Converters;

namespace Declutterer.Tests.Converters;

public class AssessmentConverterTests
{
    private readonly AssessmentConverter _converter = new();

    private static JunkAssessment Make(CleanupTier tier) => new()
    {
        JunkScore = 0.97, DeletionRisk = 0.03, Confidence = 0.8, Tier = tier,
        Reasons = new[] { new Signal(ReasonCode.KnownCacheLocation, EvidenceGroup.Location, 4.0, 0.02, "browser cache") },
    };

    [Fact]
    public void Label_ShowsTierAndJunkPercentage()
    {
        var result = _converter.Convert(Make(CleanupTier.Safe), typeof(string), "label", CultureInfo.InvariantCulture);

        Assert.Equal("Safe to delete · 97%", result);
    }

    [Fact]
    public void Tip_ListsTheReasons()
    {
        var result = _converter.Convert(Make(CleanupTier.Safe), typeof(string), "tip", CultureInfo.InvariantCulture);

        Assert.Contains("Known cache location", Assert.IsType<string>(result));
        Assert.Contains("browser cache", (string)result!);
    }

    [Theory]
    [InlineData(CleanupTier.Safe, "#2D7D2D")]
    [InlineData(CleanupTier.Likely, "#5B8C2A")]
    [InlineData(CleanupTier.Review, "#B7791F")]
    [InlineData(CleanupTier.Keep, "#888888")]
    [InlineData(CleanupTier.Protected, "#B22222")]
    public void Brush_FollowsTheTier(CleanupTier tier, string expected)
    {
        var result = _converter.Convert(Make(tier), typeof(IBrush), "brush", CultureInfo.InvariantCulture);

        Assert.Equal(Color.Parse(expected), Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Fact]
    public void NotAssessed_ShowsNothing()
    {
        Assert.Equal(string.Empty, _converter.Convert(null, typeof(string), "label", CultureInfo.InvariantCulture));
        Assert.Null(_converter.Convert(null, typeof(string), "tip", CultureInfo.InvariantCulture));
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(_converter.Convert(null, typeof(IBrush), "brush", CultureInfo.InvariantCulture)).Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() => _converter.ConvertBack("x", typeof(JunkAssessment), null, CultureInfo.InvariantCulture));
    }
}
