using System;
using System.Collections.Generic;
using System.IO;
using Declutterer.Utilities.Exceptions;

namespace Declutterer.Domain.Services.Deletion;

/// <summary>
/// Determines whether a given path is safe to delete.
/// </summary>
public static class PathSafetyValidator
{
    /// <summary>
    /// Special folders whose *contents* must also be protected (i.e. deleting any descendant
    /// is blocked, not just the root-folder itself).
    /// </summary>
    private static readonly IReadOnlySet<Environment.SpecialFolder> ProtectDescendants =
        new HashSet<Environment.SpecialFolder>
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,           // C:\Windows\System32
            Environment.SpecialFolder.SystemX86,        // C:\Windows\SysWOW64
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86,
        };

    /// <summary>
    /// Lazily-built, case-insensitive lookup of every resolved <see cref="Environment.SpecialFolder"/> path.
    /// The value indicates whether descendants of that folder are also protected.
    /// </summary>
    private static readonly Lazy<Dictionary<string, bool>> SpecialFolderPaths = new(() =>
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // enumerating over every enum value
        foreach (Environment.SpecialFolder folder in Enum.GetValues(typeof(Environment.SpecialFolder)))
        {
            var folderPath = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(folderPath))
                continue;

            // if this folder is in the ProtectDescendants set, mark it as such
            var protectDescendants = ProtectDescendants.Contains(folder); 

            // keeping the strongest protection if the same path appears under multiple enum values.
            if (!map.TryGetValue(folderPath, out var existing) || (!existing && protectDescendants))
                map[folderPath] = protectDescendants;
        }

        return map;
    });

    /// <summary>
    /// Validates that a path is safe to delete using two complementary checks:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <b>FileAttributes.System check</b> — walks from the target up through its ancestor
    ///       directories (excluding the filesystem root). If any entry carries the System attribute
    ///       the deletion is blocked.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Special-folder check</b> — compares the target against every path returned by
    ///       <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> (resolved at runtime, cross-platform).
    ///       Deleting a special folder itself is always blocked.  For a curated subset of OS-critical
    ///       folders (Windows, System32, Program Files …) deleting any descendant is also blocked.
    ///     </description>
    ///   </item>
    /// </list>
    /// Throws <see cref="OperationFailedException"/> when either check fails.
    /// </summary>
    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        var fullPath = Path.GetFullPath(path);

        // ── Check 1: FileAttributes.System ──────────────────────────────────────
        // Walk from the target up to (but NOT including) the filesystem root.
        // Drive roots (e.g. C:\) always carry FileAttributes.System on Windows, so
        // we stop before reaching them to avoid blocking every single deletion.
        var current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            var parent = Path.GetDirectoryName(current);

            // Stop before checking the filesystem root (parent is null or equal to current).
            if (parent == null || parent == current)
                break;

            try
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.System))
                {
                    throw new OperationFailedException(
                        $"Cannot delete '{path}': '{current}' has the System attribute set. " +
                        $"This operation has been blocked for safety reasons.");
                }
            }
            catch (OperationFailedException)
            {
                throw;
            }
            catch
            {
                // Cannot read attributes (e.g. access denied) — stop walking and let
                // the actual delete operation surface the real error.
                break;
            }

            current = parent;
        }

        // ── Check 2: Environment.SpecialFolder ──────────────────────────────────
        // Resolved at runtime so it works correctly on every platform and account.
        var specialFolders = SpecialFolderPaths.Value;
        
        foreach (var (specialPath, protectDescendants) in specialFolders)
        {
            // Always blocking deletion of the special folder itself, e.g. "C:\Windows" (this is not a directory that has the System attribute; it is only considered a 'system' folder).
            var isExactMatch = string.Equals(fullPath, specialPath, StringComparison.OrdinalIgnoreCase);

            // For OS-critical folders also block any descendant. e.g. "C:\Windows\System32\drivers\etc\hosts"
            var isDescendant = protectDescendants &&
                               fullPath.StartsWith(specialPath + Path.DirectorySeparatorChar,
                                   StringComparison.OrdinalIgnoreCase);

            if (isExactMatch || isDescendant)
            {
                throw new OperationFailedException(
                    $"Cannot delete '{path}': it is or resides inside the protected system folder " +
                    $"'{specialPath}'. This operation has been blocked for safety reasons.");
            }
        }
    }
}

