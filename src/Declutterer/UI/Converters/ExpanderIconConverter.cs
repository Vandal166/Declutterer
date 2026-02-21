using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace Declutterer.UI.Converters;

/// <summary>
/// Converts IsExpanded boolean to the appropriate arrow icon.
/// </summary>
public class ExpanderIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;
        }
        return MaterialIconKind.ChevronRight;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
