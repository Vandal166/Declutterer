using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace Declutterer.UI.Converters;

/// <summary>
/// Converts a boolean IsDirectory to the appropriate MaterialIcon kind (folder or file).
/// </summary>
public class FolderOrFileIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirectory)
        {
            return isDirectory ? MaterialIconKind.Folder : MaterialIconKind.FileDocument;
        }

        return MaterialIconKind.FileDocument;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

