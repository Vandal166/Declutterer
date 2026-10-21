using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup.Rules;

namespace Declutterer.Domain.Services.Cleanup;

public enum DirectorySafetyStatus
{
    Safe,
    Blocked,
    Incomplete,
}

public sealed record DirectorySafetyScanResult(
    DirectorySafetyStatus Status,
    string? BlockReason,
    int EntriesVisited,
    bool BudgetExceeded)
{
    public bool CanAutoSelect => Status == DirectorySafetyStatus.Safe;
}

/// <summary>
/// Performs the bounded, exhaustive path and content checks needed before a directory can represent its whole subtree
/// in Smart Select. It never follows reparse points. A known blocker is sufficient to reject the directory; a clean
/// result requires visiting every descendant within the configured bounds.
/// </summary>
public sealed class DirectorySafetyScanner
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        ReturnSpecialDirectories = false,
    };

    private static readonly SensitiveLocationRule SensitiveLocations = new();
    private static readonly OwnershipRule Ownership = new();
    private static readonly ContentRule Content = new();

    // None of the rules run here consult the marker cache, so one shared (empty) instance is enough. Creating a
    // MarkerCache per visited entry allocated a ConcurrentDictionary for every file of every scanned subtree.
    private static readonly MarkerCache NoMarkers = new();

    public DirectorySafetyScanResult Scan(string path, ISystemSnapshot machine, SmartCleanupOptions options,
        DateTime deadlineUtc, Func<string, string?> safetyGate, CancellationToken cancellationToken = default)
    {
        var pending = new Stack<(string Path, int Depth)>();
        var visited = 0;
        string root;
        try
        {
            root = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsFilesystemAccessException(ex))
        {
            return Incomplete($"could not resolve '{path}' ({ex.Message})", visited);
        }

        try
        {
            var rootAttributes = File.GetAttributes(root);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
                return Blocked($"is a reparse point at '{root}'", visited);
            if ((rootAttributes & FileAttributes.Directory) == 0)
                return Blocked($"is not a directory at '{root}'", visited);
        }
        catch (Exception ex) when (IsFilesystemAccessException(ex))
        {
            return Incomplete($"could not inspect '{root}' ({ex.Message})", visited);
        }

        var rootBlock = InspectPath(root, isDirectory: true, machine, options, safetyGate);
        if (rootBlock is not null)
            return Blocked(rootBlock, visited);

        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadlineUtc)
                return Incomplete("time budget reached before the descendant safety scan completed", visited, budgetExceeded: true);

            var (directoryPath, depth) = pending.Pop();
            if (depth >= options.MaxDepth)
                return Incomplete("directory depth limit reached before the descendant safety scan completed", visited);

            try
            {
                foreach (var entry in new DirectoryInfo(directoryPath).EnumerateFileSystemInfos("*", Enumeration))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > options.MaxEntriesPerDirectory)
                        return Incomplete("entry limit reached before the descendant safety scan completed", visited);
                    if ((visited & 0xFF) == 0 && DateTime.UtcNow >= deadlineUtc)
                        return Incomplete("time budget reached before the descendant safety scan completed", visited, budgetExceeded: true);

                    FileAttributes attributes;
                    try
                    {
                        attributes = entry.Attributes;
                    }
                    catch (Exception ex) when (IsFilesystemAccessException(ex))
                    {
                        return Incomplete($"could not inspect '{entry.FullName}' ({ex.Message})", visited);
                    }

                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        return Blocked($"contains a reparse point at '{entry.FullName}'", visited);

                    var validatePath = isDirectory || (attributes & FileAttributes.System) != 0;
                    var block = InspectPath(entry.FullName, isDirectory, machine, options, safetyGate, validatePath);
                    if (block is not null)
                        return Blocked(block, visited);

                    if (ProjectMarkerProbe.FromEntryName(entry.Name, isDirectory).HasFlag(ProjectMarkers.Git))
                        return Blocked($"contains Git repository metadata at '{entry.FullName}'", visited);

                    if (isDirectory)
                    {
                        int childDepth = depth + 1;
                        if (childDepth >= options.MaxDepth)
                            return Incomplete("directory depth limit reached before the descendant safety scan completed", visited);
                        pending.Push((entry.FullName, childDepth));
                    }
                }
            }
            catch (Exception ex) when (IsFilesystemAccessException(ex))
            {
                return Incomplete($"could not enumerate '{directoryPath}' ({ex.Message})", visited);
            }
        }

        if (DateTime.UtcNow >= deadlineUtc)
            return Incomplete("time budget reached before the descendant safety scan completed", visited, budgetExceeded: true);

        return new DirectorySafetyScanResult(DirectorySafetyStatus.Safe, null, visited, false);
    }

    private static string? InspectPath(string path, bool isDirectory, ISystemSnapshot machine,
        SmartCleanupOptions options, Func<string, string?> safetyGate, bool validatePath = true)
    {
        if (ProjectMarkerProbe.FromEntryName(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), isDirectory)
            .HasFlag(ProjectMarkers.Git))
            return $"is Git repository metadata at '{path}'";

        if (validatePath)
        {
            string? pathBlock;
            try
            {
                pathBlock = safetyGate(path);
            }
            catch (Exception ex)
            {
                pathBlock = $"path could not be validated ({ex.Message})";
            }
            if (!string.IsNullOrWhiteSpace(pathBlock))
                return pathBlock;
        }

        var node = new NodeSnapshot
        {
            Path = path,
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            IsDirectory = isDirectory,
            Depth = 0,
        };
        var context = new AssessmentContext(node, machine, options, NoMarkers, null, accessTimeReliable: false);
        var signals = new SignalSink();
        SensitiveLocations.Evaluate(context, signals);
        Ownership.Evaluate(context, signals);
        if (!isDirectory)
            Content.Evaluate(context, signals);

        var blockers = signals.Signals.Where(s => s.HardKeep || s.Code is
            ReasonCode.BrowserProfile or ReasonCode.UserSettingsOrSaves);
        var blocker = blockers.FirstOrDefault();
        return !blockers.Any() ? null : $"{blocker.Code}: {blocker.Detail} at '{path}'";
    }

    private static bool IsFilesystemAccessException(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or SecurityException;

    private static DirectorySafetyScanResult Blocked(string reason, int visited) =>
        new(DirectorySafetyStatus.Blocked, reason, visited, false);

    private static DirectorySafetyScanResult Incomplete(string reason, int visited, bool budgetExceeded = false) =>
        new(DirectorySafetyStatus.Incomplete, reason, visited, budgetExceeded);
}
