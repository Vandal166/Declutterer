using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Utilities.Exceptions;
using Microsoft.VisualBasic.FileIO;
using Serilog;

namespace Declutterer.Domain.Services.Deletion;

public sealed class DeleteService : IDeleteService
{
    private readonly IDeletionHistoryRepository _historyRepository;

    public DeleteService(IDeletionHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
    }

    public async Task<DeleteResult> MoveToRecycleBinAsync(ObservableCollection<TreeNode> items,
        IProgress<DeleteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new DeleteResult();
        var totalItems = items.Count;
        var processedCount = 0;

        // Create a list of items to delete to avoid collection modified exceptions
        var itemsToProcess = items.ToList();

        foreach (var item in itemsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = item.FullPath;

                // Report progress
                processedCount++;
                var progressData = new DeleteProgress
                {
                    ProcessedItemCount = processedCount,
                    TotalItemCount = totalItems,
                    CurrentItemPath = path,
                    ProgressPercentage = (processedCount / (double)totalItems) * 100
                };
                progress?.Report(progressData);

                // Move to Recycle Bin using platform-specific methods
                await MoveToRecycleBinAsync(path);
                Log.Information("Moved to recycle bin: {Path}", path);

                result.DeletedCount++;
                result.TotalBytesFreed += GetTotalSize(item);

                // Record to deletion history
                await RecordDeletionHistoryAsync(item, "RecycleBin");

                // Remove from the collection ONLY after successful deletion
                items.Remove(item);
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error moving item to recycle bin: {Path}", item.FullPath);
                result.FailedCount++;
                result.Errors.Add(
                    new DeletionError
                    {
                        ItemPath = item.FullPath,
                        ErrorMessage = ex.Message,
                        Exception = ex
                    });
            }
        }

        result.Success = result.FailedCount == 0;
        return result;
    }

    public async Task<DeleteResult> DeletePermanentlyAsync(ObservableCollection<TreeNode> items,
        IProgress<DeleteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new DeleteResult();
        var totalItems = items.Count;
        var processedCount = 0;

        // Create a list of items to delete to avoid collection modified exceptions
        var itemsToProcess = items.ToList();

        foreach (var item in itemsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = item.FullPath;

                // Report progress
                processedCount++;
                var progressData = new DeleteProgress
                {
                    ProcessedItemCount = processedCount,
                    TotalItemCount = totalItems,
                    CurrentItemPath = path,
                    ProgressPercentage = (processedCount / (double)totalItems) * 100
                };
                progress?.Report(progressData);

                // Permanently delete the item
                if (File.Exists(path))
                {
                    ValidatePathSafety(path);
                    File.Delete(path);
                    Log.Information("Permanently deleted file: {Path}", path);
                }
                else if (Directory.Exists(path))
                {
                    ValidatePathSafety(path);
                    Directory.Delete(path, recursive: true);
                    Log.Information("Permanently deleted directory: {Path}", path);
                }
                else
                {
                    throw new FileNotFoundException($"Path not found: {path}");
                }

                result.DeletedCount++;
                result.TotalBytesFreed += GetTotalSize(item);

                // Record to deletion history
                await RecordDeletionHistoryAsync(item, "Permanent");

                // Remove from the collection after successful deletion
                items.Remove(item);
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error permanently deleting item: {Path}", item.FullPath);
                result.FailedCount++;
                result.Errors.Add(
                    new DeletionError
                    {
                        ItemPath = item.FullPath,
                        ErrorMessage = ex.Message,
                        Exception = ex
                    });
            }
        }

        result.Success = result.FailedCount == 0;
        return result;
    }

    private static long GetTotalSize(TreeNode node) => node.Size;

    /// <summary>
    /// Cross-platform method to move a file or directory to the recycle bin / trash.
    /// Uses platform-specific native commands or APIs.
    /// </summary>
    private static async Task MoveToRecycleBinAsync(string path)
    {
        ValidatePathSafety(path);

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"Path not found: {path}");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await MoveToRecycleBinWindowsAsync(path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await MoveToRecycleBinLinuxAsync(path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await MoveToRecycleBinMacAsync(path);
        }
        else
        {
            throw new PlatformNotSupportedException($"Recycle bin operation not supported on this platform");
        }
    }

    /// <summary>
    /// Windows: Use Microsoft.VisualBasic.FileIO to move files/directories to Recycle Bin.
    /// This is the most reliable method for Windows systems.
    /// </summary>
    private static async Task MoveToRecycleBinWindowsAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    FileSystem.DeleteFile(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                }
                else if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Windows Recycle Bin operation failed");
                throw;
            }
        });
    }

    /// <summary>
    /// Linux: Use 'gio trash' command (GNOME) or 'trash-cli' as fallback.
    /// </summary>
    private static async Task MoveToRecycleBinLinuxAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                // Try gio trash first (GNOME/GTK-based desktops)
                var result = ExecuteCommand("gio", $"trash \"{path}\"");
                if (result.Success)
                    return;

                // Fallback to trash-cli
                result = ExecuteCommand("trash-put", $"\"{path}\"");
                if (result.Success)
                    return;

                // Both methods failed - throw an exception to inform caller
                throw new OperationFailedException(
                    $"Failed to move '{path}' to trash on Linux. Neither 'gio trash' nor 'trash-put' succeeded. " +
                    $"Ensure GNOME desktop or trash-cli is installed. Error: {result.Error}");
            }
            catch (OperationFailedException)
            {
                throw; // Re-throw our custom exception
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Linux recycle bin operation failed with unexpected error");
                throw new OperationFailedException(
                    $"Unexpected error moving '{path}' to trash on Linux: {ex.Message}",
                    ex);
            }
        });
    }

    /// <summary>
    /// macOS: Use 'rmtrash' or AppleScript via osascript as fallback.
    /// </summary>
    private static async Task MoveToRecycleBinMacAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                // Try rmtrash first (if installed)
                var result = ExecuteCommand("rmtrash", $"\"{path}\"");
                if (result.Success)
                    return;

                // Fallback to AppleScript via osascript
                var escapedPath = path.Replace("\"", "\\\"");
                var appleScript = $"tell application \"Finder\" to delete POSIX file \"{escapedPath}\"";
                result = ExecuteCommand("osascript", $"-e \"{appleScript}\"");

                if (result.Success)
                    return;

                // Both methods failed - throw an exception to inform caller
                throw new OperationFailedException(
                    $"Failed to move '{path}' to trash on macOS. Neither 'rmtrash' nor 'osascript' succeeded. " +
                    $"Error: {result.Error}");
            }
            catch (OperationFailedException)
            {
                throw; // Re-throw our custom exception
            }
            catch (Exception ex)
            {
                Log.Error(ex, "macOS recycle bin operation failed with unexpected error");
                throw new OperationFailedException(
                    $"Unexpected error moving '{path}' to trash on macOS: {ex.Message}",
                    ex);
            }
        });
    }

    /// <summary>
    /// Helper to execute a system command and return result.
    /// </summary>
    private static (bool Success, string Output, string Error) ExecuteCommand(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                    return (false, "", "Failed to start process");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                return (process.ExitCode == 0, output, error);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Command execution failed: {Command}", command);
            return (false, "", ex.Message);
        }
    }
    
    /// <summary>
    /// Validates that a path is safe to delete and not a critical system directory.
    /// Throws an exception if the path is deemed unsafe.
    /// </summary>
    public static void ValidatePathSafety(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        }

        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();

        // Critical system paths that should NEVER be deleted
        // Only check paths relevant to the current platform
        var criticalSystemPaths = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows-specific critical paths
            criticalSystemPaths.AddRange(
                new[]
                {
                    "c:\\",
                    "c:\\windows",
                    "c:\\program files",
                    "c:\\program files (x86)",
                    "c:\\programdata",
                    "c:\\boot",
                    "c:\\system",
                    "c:\\windows\\system32",
                    "c:\\windows\\syswow64",
                    "c:\\recovery",
                    "c:\\$recycle.bin",
                    "c:\\pagefile.sys",
                    "c:\\hiberfil.sys",
                    "c:\\swapfile.sys",
                    "c:\\documents and settings",
                    "c:\\system volume information",
                    "c:\\recycler",
                    // System user profiles only - NOT the current user's home
                    "c:\\users\\default",
                    "c:\\users\\default user",
                    "c:\\users\\public",
                    "c:\\users\\all users",
                    // Other drive roots
                    "d:\\",
                    "e:\\",
                    "f:\\",
                    "g:\\",
                    "h:\\",
                });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux-specific critical paths
            criticalSystemPaths.AddRange(
                new[]
                {
                    "/",
                    "/bin",
                    "/sbin",
                    "/usr",
                    "/usr/bin",
                    "/usr/sbin",
                    "/usr/local",
                    "/usr/share",
                    "/etc",
                    "/sys",
                    "/proc",
                    "/root",
                    "/boot",
                    "/lib",
                    "/lib64",
                    "/var",
                    "/var/log",
                    "/var/run",
                    "/var/tmp",
                    "/home",
                    "/dev",
                    "/mnt",
                    "/media",
                    "/opt",
                    "/srv",
                    "/run",
                    "/lost+found",
                    "/tmp",
                });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS-specific critical paths
            criticalSystemPaths.AddRange(
                new[]
                {
                    "/",
                    "/bin",
                    "/sbin",
                    "/usr",
                    "/usr/bin",
                    "/usr/sbin",
                    "/usr/local",
                    "/usr/share",
                    "/etc",
                    "/sys",
                    "/proc",
                    "/root",
                    "/boot",
                    "/lib",
                    "/var",
                    "/var/log",
                    "/var/run",
                    "/var/tmp",
                    "/home",
                    "/dev",
                    "/mnt",
                    "/media",
                    "/opt",
                    "/srv",
                    "/run",
                    "/lost+found",
                    "/tmp",
                    "/Applications",
                    "/Library",
                    "/System",
                    "/Volumes",
                    "/Network",
                    "/private",
                    "/cores",
                    "/var/folders",
                });
        }

        // Check against critical system paths
        foreach (var dangerousPath in criticalSystemPaths)
        {
            var normalizedDangerous = dangerousPath.ToLowerInvariant();

            // On Windows, ensure we're comparing Windows paths properly
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                normalizedDangerous = normalizedDangerous.Replace("/", "\\");
            }

            // Check if the path is exactly a system directory or a parent directory of it
            if (normalizedPath == normalizedDangerous ||
                normalizedPath.StartsWith(normalizedDangerous + Path.DirectorySeparatorChar))
            {
                throw new OperationFailedException(
                    $"Cannot delete system-critical directory: {path}. " +
                    $"This operation has been blocked for safety reasons.");
            }
        }

        // Additional check: path should be reasonably deep (not root or top-level only)
        var pathDepth = normalizedPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
        if (pathDepth < 3 && (normalizedPath.EndsWith($"{Path.DirectorySeparatorChar}") || normalizedPath.Length <= 3))
        {
            throw new OperationFailedException(
                $"Cannot delete root or top-level system directory: {path}. " +
                $"This operation has been blocked for safety reasons.");
        }
    }

    /// <summary>
    /// Records a deletion event to the history repository.
    /// Errors are logged but do not block the deletion operation.
    /// </summary>
    private async Task RecordDeletionHistoryAsync(TreeNode item, string deletionType)
    {
        try
        {
            var historyEntry = new DeletionHistoryEntry
            {
                Name = item.Name,
                Path = item.FullPath,
                SizeBytes = item.Size,
                DeletionDateTime = DateTime.Now,
                DeletionType = deletionType,
                IsDirectory = item.IsDirectory,
                ParentPath = Path.GetDirectoryName(item.FullPath)
            };

            await _historyRepository.AddEntryAsync(historyEntry);
        }
        catch (Exception ex)
        {
            // Log the error but don't throw - deletion succeeded, history recording is secondary
            Log.Warning(ex, "Failed to record deletion history for {Path}", item.FullPath);
        }
    }
}
