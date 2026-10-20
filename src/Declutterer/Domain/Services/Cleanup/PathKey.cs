using System;
using System.Collections.Generic;
using System.IO;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>Small, allocation-friendly path helpers shared by the Smart Select engine.</summary>
public static class PathKey
{
    private static readonly char Sep = Path.DirectorySeparatorChar;

    /// <summary>Trims quotes/whitespace, unifies separators and removes trailing separators.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var p = path.Trim().Trim('"').Replace('/', Sep).Replace('\\', Sep);
        // keep a lone root ("/" or "C:\") intact but drop other trailing separators
        while (p.Length > 1 && p[^1] == Sep && !(p.Length == 3 && p[1] == ':'))
            p = p[..^1];
        return p;
    }

    public static string[] Segments(string path) =>
        Normalize(path).Split(Sep, StringSplitOptions.RemoveEmptyEntries);

    public static bool IsUnderOrEqual(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            return false;

        var p = Normalize(path);
        var r = Normalize(root);
        if (p.Length < r.Length)
            return false;
        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            return false;
        return p.Length == r.Length || r[^1] == Sep || p[r.Length] == Sep;
    }

    /// <summary>Yields the path itself followed by every ancestor (excluding an empty string).</summary>
    public static IEnumerable<string> SelfAndAncestors(string path)
    {
        var p = Normalize(path);
        while (p.Length > 0)
        {
            yield return p;
            var idx = p.LastIndexOf(Sep);
            if (idx <= 0)
                yield break;
            p = p[..idx];
        }
    }

    /// <summary>True if <paramref name="pattern"/> occurs as a contiguous run of segments (case-insensitive).</summary>
    public static bool ContainsSequence(string[] segments, string[] pattern)
    {
        if (pattern.Length == 0 || pattern.Length > segments.Length)
            return false;

        for (int i = 0; i <= segments.Length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && string.Equals(segments[i + j], pattern[j], StringComparison.OrdinalIgnoreCase))
                j++;
            if (j == pattern.Length)
                return true;
        }
        return false;
    }

    /// <summary>Lower-cased alphanumerics only. "Battle.net (64-bit)" -> "battlenet64bit".</summary>
    public static string AlphaNumKey(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        Span<char> buf = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        int n = 0;
        foreach (var c in text)
            if (char.IsLetterOrDigit(c))
                buf[n++] = char.ToLowerInvariant(c);
        return new string(buf[..n]);
    }
}
