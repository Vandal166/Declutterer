using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Declutterer.UI.Converters;

/// <summary>
/// Converts the current sort column name and direction to a visual indicator (▲ or ▼).
/// </summary>
public class SortIndicatorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var currentSort = value as string;
        var columnName = parameter as string;

        // If this isn't the sorted column, return empty
        if (currentSort != columnName)
        {
            return string.Empty;
        }

        // Return arrow based on sort direction
        // This assumes we have access to SortAscending, but we'll use a simple indicator for now
        return "▼"; // We'll enhance this later with sort direction info if needed
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

