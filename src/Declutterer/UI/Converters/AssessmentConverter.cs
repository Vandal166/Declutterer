using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Declutterer.Domain.Models;

namespace Declutterer.UI.Converters;

/// <summary>
/// Presents a <see cref="JunkAssessment"/> in the tree: ConverterParameter "label" (tier text), "brush" (tier colour),
/// "tip" (multi-line explanation for the tooltip). A node that was not assessed shows nothing.
/// </summary>
public class AssessmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var assessment = value as JunkAssessment;

        return parameter as string switch
        {
            "label" => assessment is null ? string.Empty : $"{assessment.TierLabel} · score {(int)Math.Round(assessment.JunkScore * 100)}/100",
            "brush" => new SolidColorBrush(assessment is null ? Colors.Transparent : ColorFor(assessment.Tier)),
            "tip" => assessment?.Explain(),
            _ => assessment?.Summary,
        };
    }

    public static Color ColorFor(CleanupTier tier) => tier switch
    {
        CleanupTier.Safe => Color.Parse("#2D7D2D"),
        CleanupTier.Likely => Color.Parse("#5B8C2A"),
        CleanupTier.Review => Color.Parse("#B7791F"),
        CleanupTier.Protected => Color.Parse("#B22222"),
        _ => Color.Parse("#888888"),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
