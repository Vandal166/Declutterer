using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Integration.SystemInfo;

/// <summary>
/// Conservative fallback for Linux / macOS until dedicated providers exist: knows temp and application-data roots and
/// the running processes, but has no installed-program inventory, so "leftovers of removed applications" stays disabled.
/// </summary>
public sealed class DefaultSystemSnapshotProvider : ISystemSnapshotProvider
{
    public Task<ISystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var data = new SystemSnapshotData { UtcNow = DateTime.UtcNow };

        data.TempRoots.Add(Path.GetTempPath());

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            foreach (var rel in new[] { ".config", ".local/share", ".cache", "Library/Application Support", "Library/Caches" })
                data.AppDataRoots.Add(Path.Combine(home, rel));
            data.ContainerRoots.Add(home);
            foreach (var rel in new[] { "Downloads", "Documents", "Desktop", "Pictures", "Music", "Videos" })
                data.ContainerRoots.Add(Path.Combine(home, rel));
            foreach (var rel in new[] { "Dropbox", "iCloudDrive", "Library/Mobile Documents", "OneDrive" })
                data.SyncedRoots.Add(Path.Combine(home, rel));
        }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var file = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(file))
                    data.RunningExecutables.Add(file);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // ignore protected processes
            }
            finally
            {
                p.Dispose();
            }
        }

        return Task.FromResult<ISystemSnapshot>(new SystemSnapshot(data));
    }
}
