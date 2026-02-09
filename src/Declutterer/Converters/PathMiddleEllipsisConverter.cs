using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Declutterer.Converters;

public class PathMiddleEllipsisConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return value;

        // Get the max length from parameter, default to 50
        int maxLength = 50;
        if (parameter is string paramStr && int.TryParse(paramStr, out var len))
        {
            maxLength = len;
        }

        if (path.Length <= maxLength)
            return path;

        // Extract the last segment (directory/file name)
        string lastSegment = Path.GetFileName(path);
        if (string.IsNullOrEmpty(lastSegment))
        {
            // Handle cases like "C:\" or "C:\Users\" where GetFileName might return empty
            lastSegment = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        }

        // If just the last segment is already longer than maxLength, return it anyway
        if (lastSegment.Length > maxLength)
            return lastSegment;

        // Reserve space for ellipsis and the last segment
        int ellipsisLength = 4; // "…"
        int availableForPrefix = maxLength - lastSegment.Length - ellipsisLength;

        if (availableForPrefix < 1)
        {
            // Not enough space, just show "…" + last segment
            return $"…{lastSegment}";
        }

        string prefix = path.Substring(0, Math.Min(availableForPrefix, path.Length - lastSegment.Length - 1));

        return $"{prefix}…{lastSegment}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}
