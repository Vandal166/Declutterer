using System;
using System.Globalization;
// ReSharper disable SuggestVarOrType_BuiltInTypes

namespace Declutterer.Benchmark.Common;

/// <summary>
/// Class for converting byte counts to human-readable formats.
/// </summary>
public static class ConvertBytes
{
    private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB" };
    
    // returns value - suffix
    public static ValueTuple<long, string> ToReadableTuple(long byteCount)
    {
        if (byteCount == 0)
            return (0, Suffixes[0]);
        
        long bytes = Math.Abs(byteCount);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Convert.ToInt64(Math.Sign(byteCount) * num), Suffixes[place]);
    }
    
    /// <summary>
    /// Converts a byte count to a human-readable string with appropriate units.
    /// </summary>
    /// <param name="byteCount">The byte count to convert.</param>
    /// <returns>A string representation of the byte count in a human-readable format.</returns>
    public static string ToReadableString(long byteCount)
    {
        if (byteCount == 0)
            return "0" + Suffixes[0];
        
        long bytes = Math.Abs(byteCount);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + " " + Suffixes[place];
    }
}
