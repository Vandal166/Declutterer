using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Declutterer.Converters;

public sealed class EqualToZeroToBooleanConverter : IValueConverter
{
    // From int to bool
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue == 0;
        }

        return false;
    }

    // From bool to int (not implemented because not needed)
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}