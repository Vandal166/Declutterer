using System;

namespace Declutterer.Abstractions;

/// <summary>
/// Read-only, point-in-time knowledge about the machine that the Smart Select engine needs:
/// what is installed, what is running, what the user opened recently, where temp/cloud folders are.
/// Captured once per Smart Select run so that every rule sees a consistent view.
/// </summary>
public interface ISystemSnapshot
{
    DateTime UtcNow { get; }

    /// <summary>True when the OS reports that last-access timestamps are maintained.</summary>
    bool LastAccessTimeReliable { get; }

    /// <summary>
    /// True when a usable list of installed programs is available. Without it "orphaned app data" can not be judged
    /// (everything would look orphaned), so the corresponding rule stays silent.
    /// </summary>
    bool HasInstalledAppInventory { get; }

    bool IsUnderTempRoot(string path);

    /// <summary>True if <paramref name="path"/> lives below a per-user / machine wide application data root (AppData, ProgramData ...).</summary>
    bool IsUnderAppDataRoot(string path, out string root);

    /// <summary>
    /// The path IS a system / user container folder (temp folder, AppData root, Downloads, a cloud sync root ...).
    /// Such a folder must never be deleted itself - only its contents are candidates.
    /// </summary>
    bool IsContainerRoot(string path, out string description);

    /// <summary>True if the path is inside a cloud-synced folder (OneDrive, Dropbox ...) where deleting propagates to the cloud.</summary>
    bool IsInSyncedFolder(string path, out string provider);

    /// <summary>The path equals / lies inside / contains the install location of an installed program.</summary>
    bool TryGetInstalledAppAt(string path, out string appName);

    /// <summary>A currently running program's executable lives in (or below) this path.</summary>
    bool IsInRunningProcessPath(string path);

    /// <summary>A startup (Run key) or service entry points into (or below) this path.</summary>
    bool IsReferencedByStartupOrService(string path);

    /// <summary>The user opened this file, or a file inside this directory, via the shell (Recent items).</summary>
    bool TryGetRecentOpen(string path, out DateTime openedUtc);

    /// <summary>Does a folder / vendor name correspond to an installed program (fuzzy)?</summary>
    bool MatchesInstalledApp(string folderOrVendorName, out string appName);

    /// <summary>Does a file name (e.g. an installer "Zoom_x64.msi") mention an installed program?</summary>
    bool FileNameMentionsInstalledApp(string fileName, out string appName);
}
