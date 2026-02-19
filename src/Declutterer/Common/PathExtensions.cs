using System.IO;

namespace Declutterer.Common;

public static class PathExtensions
{
    /// <summary>
    /// Normalizes a path by removing trailing directory separators for consistent comparison.
    /// </summary>
    public static string NormalizePath(this string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Truncates a path string to a maximum length, replacing the middle with ellipsis.
    /// </summary>
    /// <param name="path">The path to truncate</param>
    /// <param name="maxLength">Maximum length for the result</param>
    /// <returns>Truncated path with ellipsis in the middle if needed</returns>
    public static string GetMiddleEllipsis(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;

        int ellipsisLength = 3; // "..."
        int remainingLength = maxLength - ellipsisLength;
        int startLength = remainingLength / 2;
        int endLength = remainingLength - startLength;

        return path.Substring(0, startLength) + "..." + path.Substring(path.Length - endLength);
    }
}
