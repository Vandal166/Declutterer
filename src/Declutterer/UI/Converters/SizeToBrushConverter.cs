using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Declutterer.UI.Converters;

/// <summary>
/// Converts a byte size to a brush color for visual emphasis based on file size magnitude.
/// Color gradient: Grey for KB, Black for MB, Orange for GB, Red for TB+
/// </summary>
public class SizeToBrushConverter : IValueConverter
{
    // Size thresholds in bytes
    private const long MegabytesThreshold = 1024 * 1024; // 1 MB
    private const long LargeFileThreshold = 250 * 1024 * 1024; // 250 MB
    private const long GigabytesThreshold = 1024 * 1024 * 1024; // 1 GB

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long byteSize)
        {
            return GetBrushForSize(byteSize);
        }

        // Default to black if value is not a long
        return new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static SolidColorBrush GetBrushForSize(long byteSize)
    {
        if (byteSize < MegabytesThreshold)
        {
            // < 1 MB - Grey
            return new SolidColorBrush(Color.Parse("#808080"));
        }

        if (byteSize < LargeFileThreshold)
        {
            // 1 MB - 249 MB - Black
            return new SolidColorBrush(Colors.Black);
        }

        if (byteSize < GigabytesThreshold)
        {
            // 250 MB - 999 MB - Dark Orange
            return new SolidColorBrush(Color.Parse("#FF8C00"));
        }

        // >= 1 GB - Crimson Red
        return new SolidColorBrush(Color.Parse("#DC143C"));
    }
}
