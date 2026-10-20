using System;
using System.IO;
using System.Text.RegularExpressions;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Integration.SystemInfo;

/// <summary>
/// Turns the free-form strings found in the registry (UninstallString, DisplayIcon, Run values, service ImagePath)
/// into a plain executable path. Pure string logic, so it is testable on every platform.
/// </summary>
public static class RegistryPathParser
{
    private static readonly Regex ExecutableToken = new(
        @"^(?<path>.*?\.(exe|dll|ico|cpl|msc|bat|cmd|sys|msi))(\s|$|,)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool LooksRooted(string s) =>
        s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/')
        || s.StartsWith(@"\\", StringComparison.Ordinal)
        || Path.IsPathRooted(s);

    /// <summary>
    /// "\"C:\\Program Files\\Foo\\foo.exe\" /silent" -> "C:\\Program Files\\Foo\\foo.exe";
    /// "C:\\Foo\\foo.exe,0" -> "C:\\Foo\\foo.exe". Returns null if no absolute path can be determined.
    /// </summary>
    public static string? ExtractExecutablePath(string? raw, string? systemRoot = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = Environment.ExpandEnvironmentVariables(raw.Trim());

        if (!string.IsNullOrEmpty(systemRoot) && s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            s = systemRoot.TrimEnd('\\') + s[@"\SystemRoot".Length..];

        string candidate;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            candidate = end > 1 ? s[1..end] : s.Trim('"');
        }
        else
        {
            var m = ExecutableToken.Match(s);
            candidate = m.Success ? m.Groups["path"].Value : s;
        }

        candidate = candidate.Trim();
        return LooksRooted(candidate) ? candidate : null;
    }

    /// <summary>The directory part of <see cref="ExtractExecutablePath"/>.</summary>
    public static string? ExtractDirectory(string? raw, string? systemRoot = null)
    {
        var exe = ExtractExecutablePath(raw, systemRoot);
        if (exe is null)
            return null;
        var dir = Path.GetDirectoryName(PathKey.Normalize(exe));
        return string.IsNullOrEmpty(dir) ? null : dir;
    }
}
