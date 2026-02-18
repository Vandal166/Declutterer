using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Declutterer.Converters;

/// <summary>
/// Converts a boolean value to a FontWeight.
/// True returns Bold, False returns Normal.
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBold)
        {
            return isBold ? FontWeight.Bold : FontWeight.Normal;
        }

        return FontWeight.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}