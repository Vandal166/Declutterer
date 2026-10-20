using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Abstractions;

namespace Declutterer.Domain.Services.Cleanup;

public sealed record InstalledApp(string Name, string? Publisher, IReadOnlyList<string> Locations);

public sealed record RecentItem(string Path, DateTime OpenedUtc);

/// <summary>Raw facts gathered by an OS specific <see cref="ISystemSnapshotProvider"/>.</summary>
public sealed class SystemSnapshotData
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    public bool LastAccessTimeReliable { get; set; }
    public bool HasInstalledAppInventory { get; set; }

    public List<string> TempRoots { get; } = new();
    public List<string> AppDataRoots { get; } = new();
    public List<string> SyncedRoots { get; } = new();

    /// <summary>Folders that are never deleted themselves (profile, Downloads, Documents ...). Temp / AppData / synced roots are added automatically.</summary>
    public List<string> ContainerRoots { get; } = new();

    public List<InstalledApp> InstalledApps { get; } = new();

    /// <summary>Extra names that prove "something with this name is installed" (Program Files folders, Start Menu entries).</summary>
    public List<string> InstalledNameHints { get; } = new();

    public List<string> RunningExecutables { get; } = new();
    public List<string> StartupAndServiceExecutables { get; } = new();
    public List<RecentItem> RecentItems { get; } = new();
}

/// <summary>
/// Platform independent <see cref="ISystemSnapshot"/> implementation. All lookups are O(1) / O(log n) so rules can
/// call them freely from parallel loops.
/// </summary>
public sealed class SystemSnapshot : ISystemSnapshot
{
    private readonly SystemSnapshotData _d;
    private readonly string[] _tempRoots;
    private readonly string[] _appDataRoots;
    private readonly string[] _syncedRoots;
    private readonly HashSet<string> _containerRoots;
    private readonly (string Path, string App)[] _installLocations;
    private readonly HashSet<string> _runningDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startupDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly (string Key, string App)[] _appKeys;      // normalized display names + vendor + first token
    private readonly HashSet<string> _nameHintKeys;

    public SystemSnapshot(SystemSnapshotData data)
    {
        _d = data;
        _tempRoots = data.TempRoots.Select(PathKey.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _appDataRoots = data.AppDataRoots.Select(PathKey.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _syncedRoots = data.SyncedRoots.Select(PathKey.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        _containerRoots = new HashSet<string>(
            data.ContainerRoots.Select(PathKey.Normalize)
                .Concat(_tempRoots).Concat(_appDataRoots).Concat(_syncedRoots)
                .Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        _installLocations = data.InstalledApps
            .SelectMany(a => a.Locations.Select(l => (Path: PathKey.Normalize(l), App: a.Name)))
            .Where(x => x.Path.Length > 0)
            .ToArray();

        foreach (var exe in data.RunningExecutables)
            AddDirectoryChain(_runningDirs, exe);
        foreach (var exe in data.StartupAndServiceExecutables)
            AddDirectoryChain(_startupDirs, exe);

        foreach (var item in data.RecentItems)
        {
            foreach (var p in PathKey.SelfAndAncestors(item.Path))
            {
                if (!_recent.TryGetValue(p, out var existing) || item.OpenedUtc > existing)
                    _recent[p] = item.OpenedUtc;
            }
        }

        var keys = new List<(string, string)>();
        foreach (var app in data.InstalledApps)
        {
            AddKey(keys, app.Name, app.Name);
            AddKey(keys, app.Publisher, app.Name);
            var first = app.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            AddKey(keys, first, app.Name);
        }
        _appKeys = keys.ToArray();
        _nameHintKeys = data.InstalledNameHints.Select(PathKey.AlphaNumKey).Where(k => k.Length >= 3).ToHashSet(StringComparer.Ordinal);
    }

    private static void AddKey(List<(string, string)> keys, string? text, string app)
    {
        var key = PathKey.AlphaNumKey(StripNoise(text));
        if (key.Length >= 3)
            keys.Add((key, app));
    }

    /// <summary>Removes "(64-bit)", "(x86)" style suffixes so "Foo (64-bit)" and the folder "Foo" compare equal.</summary>
    private static string StripNoise(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var i = text.IndexOf('(');
        return i > 0 ? text[..i] : text;
    }

    private static void AddDirectoryChain(HashSet<string> set, string exePath)
    {
        foreach (var p in PathKey.SelfAndAncestors(exePath))
            set.Add(p);
    }

    public DateTime UtcNow => _d.UtcNow;
    public bool LastAccessTimeReliable => _d.LastAccessTimeReliable;
    public bool HasInstalledAppInventory => _d.HasInstalledAppInventory;

    public bool IsUnderTempRoot(string path) => _tempRoots.Any(r => PathKey.IsUnderOrEqual(path, r));

    public bool IsUnderAppDataRoot(string path, out string root)
    {
        foreach (var r in _appDataRoots)
        {
            if (PathKey.IsUnderOrEqual(path, r))
            {
                root = r;
                return true;
            }
        }
        root = string.Empty;
        return false;
    }

    public bool IsInSyncedFolder(string path, out string provider)
    {
        foreach (var r in _syncedRoots)
        {
            if (PathKey.IsUnderOrEqual(path, r))
            {
                provider = r;
                return true;
            }
        }
        provider = string.Empty;
        return false;
    }

    public bool IsContainerRoot(string path, out string description)
    {
        var p = PathKey.Normalize(path);
        if (_containerRoots.Contains(p))
        {
            description = IsUnderTempRootExact(p) ? "the temp folder" : "a system folder";
            return true;
        }
        description = string.Empty;
        return false;
    }

    private bool IsUnderTempRootExact(string normalized) =>
        Array.Exists(_tempRoots, r => string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase));

    public bool TryGetInstalledAppAt(string path, out string appName)
    {
        foreach (var (loc, app) in _installLocations)
        {
            // inside the install location, equal to it, or an ancestor that contains it
            if (PathKey.IsUnderOrEqual(path, loc) || PathKey.IsUnderOrEqual(loc, path))
            {
                appName = app;
                return true;
            }
        }
        appName = string.Empty;
        return false;
    }

    public bool IsInRunningProcessPath(string path) => _runningDirs.Contains(PathKey.Normalize(path));

    public bool IsReferencedByStartupOrService(string path) => _startupDirs.Contains(PathKey.Normalize(path));

    public bool TryGetRecentOpen(string path, out DateTime openedUtc) =>
        _recent.TryGetValue(PathKey.Normalize(path), out openedUtc);

    public bool MatchesInstalledApp(string folderOrVendorName, out string appName)
    {
        appName = string.Empty;
        var key = PathKey.AlphaNumKey(folderOrVendorName);
        if (key.Length < 3)
            return false;

        if (_nameHintKeys.Contains(key))
        {
            appName = folderOrVendorName;
            return true;
        }

        foreach (var (appKey, app) in _appKeys)
        {
            // "code" <-> "visualstudiocode", "jetbrains" <-> "jetbrainssro". Short keys only match exactly.
            bool match = appKey == key
                         || (key.Length >= 4 && appKey.Contains(key, StringComparison.Ordinal))
                         || (appKey.Length >= 5 && key.Contains(appKey, StringComparison.Ordinal));
            if (match)
            {
                appName = app;
                return true;
            }
        }
        return false;
    }

    public bool FileNameMentionsInstalledApp(string fileName, out string appName)
    {
        appName = string.Empty;
        var key = PathKey.AlphaNumKey(System.IO.Path.GetFileNameWithoutExtension(fileName));
        if (key.Length < 4)
            return false;

        foreach (var (appKey, app) in _appKeys)
        {
            if (appKey.Length >= 4 && key.Contains(appKey, StringComparison.Ordinal))
            {
                appName = app;
                return true;
            }
        }
        return false;
    }
}
