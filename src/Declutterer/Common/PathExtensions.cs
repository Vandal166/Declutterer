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
}
