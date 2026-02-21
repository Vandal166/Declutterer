using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Declutterer.UI.Converters;

/// <summary>
/// Converts boolean IsLoading state to a visual indicator (spinner or empty).
/// Returns a TextBlock with a spinner character when loading is true, null otherwise.
/// </summary>
public class LoadingSpinnerConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLoading && isLoading)
        {
            // Return a TextBlock with a spinner character
            return new TextBlock
            {
                Text = "⟳",
                FontSize = 12,
                Margin = new Avalonia.Thickness(5),
                Foreground = new SolidColorBrush(Colors.Gray)
            };
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}



