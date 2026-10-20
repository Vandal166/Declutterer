using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Services.Cleanup;
using Microsoft.Win32;
using Serilog;

namespace Declutterer.Integration.SystemInfo;

/// <summary>
/// Collects what Windows knows about usage: installed programs (Uninstall keys), Program Files / Start Menu names,
/// running processes, Run keys / services / Startup folder, Recent items, last-access policy, cloud sync folders.
/// Every source is optional - a failing source simply contributes nothing and the corresponding rule stays quiet.
/// The snapshot is cached for a minute so repeated Smart Select clicks are instant.
/// </summary>
public sealed class WindowsSystemSnapshotProvider : ISystemSnapshotProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const int MaxRecentItems = 3000;
    private const int MinAppsForInventory = 5;

    private readonly object _gate = new();
    private ISystemSnapshot? _cached;
    private DateTime _cachedAtUtc;

    public async Task<ISystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
                return _cached;
        }

        var data = new SystemSnapshotData { UtcNow = DateTime.UtcNow };
        FillKnownFolders(data);

        if (OperatingSystem.IsWindows())
        {
            var apps = Task.Run(Safe(ReadInstalledApps), cancellationToken);
            var hints = Task.Run(Safe(ReadInstalledNameHints), cancellationToken);
            var running = Task.Run(Safe(ReadRunningExecutables), cancellationToken);
            var startup = Task.Run(Safe(ReadStartupAndServices), cancellationToken);
            var recent = Task.Run(Safe(ReadRecentItems), cancellationToken);
            var access = Task.Run(Safe(ReadLastAccessReliable), cancellationToken);

            await Task.WhenAll(apps, hints, running, startup, recent, access).ConfigureAwait(false);

            data.InstalledApps.AddRange(apps.Result);
            data.InstalledNameHints.AddRange(hints.Result);
            data.RunningExecutables.AddRange(running.Result);
            data.StartupAndServiceExecutables.AddRange(startup.Result);
            data.RecentItems.AddRange(recent.Result);
            data.LastAccessTimeReliable = access.Result;
            // Without a plausible inventory "not installed" is meaningless, so orphan detection must stay off.
            data.HasInstalledAppInventory = data.InstalledApps.Count >= MinAppsForInventory;
        }

        var snapshot = new SystemSnapshot(data);
        lock (_gate)
        {
            _cached = snapshot;
            _cachedAtUtc = DateTime.UtcNow;
        }
        return snapshot;
    }

    private static Func<T> Safe<T>(Func<T> source) where T : new() => () =>
    {
        try
        {
            return source();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "System snapshot source {Source} failed and was skipped", source.Method.Name);
            return new T();
        }
    };

    private static Func<bool> Safe(Func<bool> source) => () =>
    {
        try { return source(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "System snapshot source {Source} failed and was skipped", source.Method.Name);
            return false;
        }
    };

    // ── known folders ──────────────────────────────────────────────────────────

    private static void FillKnownFolders(SystemSnapshotData data)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        AddIfSet(data.TempRoots, Path.GetTempPath());
        AddIfSet(data.TempRoots, Environment.GetEnvironmentVariable("TEMP"));
        AddIfSet(data.TempRoots, Environment.GetEnvironmentVariable("TMP"));
        if (!string.IsNullOrEmpty(local)) AddIfSet(data.TempRoots, Path.Combine(local, "Temp"));

        AddIfSet(data.AppDataRoots, roaming);
        AddIfSet(data.AppDataRoots, local);
        AddIfSet(data.AppDataRoots, common);
        if (!string.IsNullOrEmpty(profile)) AddIfSet(data.AppDataRoots, Path.Combine(profile, "AppData", "LocalLow"));

        foreach (var f in new[]
                 {
                     Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.UserProfile,
                 })
            AddIfSet(data.ContainerRoots, Environment.GetFolderPath(f));
        if (!string.IsNullOrEmpty(profile))
        {
            AddIfSet(data.ContainerRoots, Path.Combine(profile, "Downloads"));
            AddIfSet(data.ContainerRoots, Path.Combine(profile, "AppData"));
        }

        AddIfSet(data.SyncedRoots, Environment.GetEnvironmentVariable("OneDrive"));
        AddIfSet(data.SyncedRoots, Environment.GetEnvironmentVariable("OneDriveConsumer"));
        AddIfSet(data.SyncedRoots, Environment.GetEnvironmentVariable("OneDriveCommercial"));
        if (!string.IsNullOrEmpty(profile) && Directory.Exists(profile))
        {
            foreach (var name in new[] { "Dropbox", "iCloudDrive", "Box", "Google Drive", "My Drive", "MEGA", "pCloudDrive" })
            {
                var p = Path.Combine(profile, name);
                if (Directory.Exists(p)) AddIfSet(data.SyncedRoots, p);
            }
            try
            {
                foreach (var d in Directory.EnumerateDirectories(profile, "OneDrive*"))
                    AddIfSet(data.SyncedRoots, d);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ignore */ }
        }
    }

    private static void AddIfSet(List<string> list, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            list.Add(path);
    }

    // ── installed programs ─────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static List<InstalledApp> ReadInstalledApps()
    {
        var result = new List<InstalledApp>();
        var excluded = ExcludedLocationRoots();
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Registry64),
                 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(sub);
            if (uninstall is null)
                continue;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display))
                    continue;

                var publisher = key.GetValue("Publisher") as string;
                var locations = new List<string>();
                AddLocation(locations, (key.GetValue("InstallLocation") as string)?.Trim().Trim('"'), excluded);
                AddLocation(locations, RegistryPathParser.ExtractDirectory(key.GetValue("DisplayIcon") as string), excluded);
                AddLocation(locations, RegistryPathParser.ExtractDirectory(key.GetValue("UninstallString") as string), excluded);

                result.Add(new InstalledApp(display.Trim(), publisher?.Trim(), locations.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
            }
        }
        return result;
    }

    /// <summary>Roots that are "too broad" to be an application's own install location.</summary>
    private static HashSet<string> ExcludedLocationRoots()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[]
                 {
                     Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
                     Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
                     Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ApplicationData,
                     Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments,
                 })
        {
            var p = Environment.GetFolderPath(f);
            if (!string.IsNullOrEmpty(p)) set.Add(PathKey.Normalize(p));
        }
        return set;
    }

    private static void AddLocation(List<string> list, string? path, HashSet<string> excluded)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var p = PathKey.Normalize(path);
        if (!RegistryPathParser.LooksRooted(p) || PathKey.Segments(p).Length < 2)
            return; // drive roots and the like
        if (excluded.Contains(p))
            return;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && PathKey.IsUnderOrEqual(p, windows))
            return; // C:\Windows\Installer\{GUID} icon caches etc.

        list.Add(p);
    }

    private static List<string> ReadInstalledNameHints()
    {
        var names = new List<string>();

        void AddChildNames(string? dir, int depth)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    var isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                    names.Add(isDir ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name));
                    if (isDir && depth > 1)
                        AddChildNames(entry.FullName, depth - 1);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ignore */ }
        }

        AddChildNames(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 1);
        AddChildNames(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 1);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) AddChildNames(Path.Combine(local, "Programs"), 1);

        AddChildNames(Environment.GetFolderPath(Environment.SpecialFolder.Programs), 2);          // user Start Menu\Programs
        AddChildNames(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), 2);    // all users Start Menu\Programs

        return names;
    }

    // ── running programs ───────────────────────────────────────────────────────

    private static List<string> ReadRunningExecutables()
    {
        var result = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var file = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(file))
                    result.Add(file);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // protected / exited process: its folder is protected by other rules anyway
            }
            finally
            {
                p.Dispose();
            }
        }
        return result;
    }

    // ── startup entries and services ───────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static List<string> ReadStartupAndServices()
    {
        var result = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        void ReadRunKey(RegistryHive hive, RegistryView view, string subKey)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            if (key is null) return;
            foreach (var valueName in key.GetValueNames())
            {
                var exe = RegistryPathParser.ExtractExecutablePath(key.GetValue(valueName) as string, windows);
                if (exe is not null) result.Add(exe);
            }
        }

        foreach (var sub in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            ReadRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, sub);
            ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, sub);
            ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, sub);
        }

        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var services = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
        {
            if (services is not null)
            {
                foreach (var name in services.GetSubKeyNames())
                {
                    using var svc = services.OpenSubKey(name);
                    var exe = RegistryPathParser.ExtractExecutablePath(svc?.GetValue("ImagePath") as string, windows);
                    if (exe is not null) result.Add(exe);
                }
            }
        }

        foreach (var startupDir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                 })
        {
            result.AddRange(ReadLinkTargets(startupDir, 200).Select(t => t.Target));
        }

        return result;
    }

    // ── recent items ───────────────────────────────────────────────────────────

    private static List<RecentItem> ReadRecentItems()
    {
        var recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        return ReadLinkTargets(recentDir, MaxRecentItems)
            .Select(t => new RecentItem(t.Target, t.WrittenUtc))
            .ToList();
    }

    private static IEnumerable<(string Target, DateTime WrittenUtc)> ReadLinkTargets(string? directory, int max)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            yield break;

        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(directory).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(max).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            string? target;
            try
            {
                // shortcuts are tiny; refuse anything unusual
                if (file.Length is 0 or > 64 * 1024) continue;
                target = LnkParser.TryGetTargetPath(File.ReadAllBytes(file.FullName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(target))
                yield return (target, file.LastWriteTimeUtc);
        }
    }

    // ── last-access policy ─────────────────────────────────────────────────────

    /// <summary>
    /// NtfsDisableLastAccessUpdate: bit 0 = disabled; bit 31 = "system managed". Only an explicit, user-managed
    /// "enabled" (0) is trusted; the system-managed default can not be relied upon, and the engine additionally
    /// probes real timestamps (see JunkAssessmentEngine.AccessTimeLooksMaintained).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool ReadLastAccessReliable()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
        return key?.GetValue("NtfsDisableLastAccessUpdate") is int v && (uint)v == 0;
    }
}
