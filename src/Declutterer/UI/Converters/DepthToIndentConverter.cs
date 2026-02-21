using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Declutterer.UI.Converters;

/// <summary>
/// Converts tree depth to indentation width (pixels).
/// </summary>
public class DepthToIndentConverter : IValueConverter
{
    private const int IndentPerLevel = 20; // pixels per depth level

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int depth)
        {
            return depth * IndentPerLevel;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

