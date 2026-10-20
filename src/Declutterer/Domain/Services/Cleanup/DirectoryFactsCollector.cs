using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// Shares completed sub-results between walks: when the walk of a parent reaches a directory that is itself an
/// analysis target and was already walked, its facts are reused instead of enumerating the subtree twice.
/// </summary>
public sealed class FactsMemo
{
    private readonly ConcurrentDictionary<string, byte> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DirectoryFacts> _facts = new(StringComparer.OrdinalIgnoreCase);

    public FactsMemo() { }

    public FactsMemo(IEnumerable<string> targetPaths) => AddTargets(targetPaths);

    /// <summary>Directories whose completed facts are worth remembering (the nodes of the tree being analysed).</summary>
    public void AddTargets(IEnumerable<string> targetPaths)
    {
        foreach (var p in targetPaths)
            _targets[PathKey.Normalize(p)] = 0;
    }

    public bool IsTarget(string path) => _targets.ContainsKey(PathKey.Normalize(path));

    public bool TryGet(string path, out DirectoryFacts facts) => _facts.TryGetValue(PathKey.Normalize(path), out facts!);

    public void Publish(string path, DirectoryFacts facts) => _facts[PathKey.Normalize(path)] = facts;
}

public sealed class FactsWalkOptions
{
    public int MaxEntries { get; init; } = 300_000;
    public int MaxDepth { get; init; } = 48;

    /// <summary>Absolute deadline; when reached the walk stops and reports <see cref="DirectoryFacts.Truncated"/>.</summary>
    public DateTime DeadlineUtc { get; init; } = DateTime.MaxValue;

    /// <summary>Include last-access timestamps in the "newest activity" computation.</summary>
    public bool IncludeAccessTime { get; init; }

    /// <summary>Include creation timestamps in the "newest activity" computation.</summary>
    public bool UseCreationTime { get; init; } = true;

    /// <summary>If set, the walk stops as soon as anything newer than this is found (the directory is provably alive).</summary>
    public DateTime? StopWhenActivityAfterUtc { get; init; }

    public FactsMemo? Memo { get; init; }
}

/// <summary>
/// Bounded, allocation-conscious subtree walker. Reparse points (junctions / symlinks) are never followed,
/// unreadable directories are counted instead of failing, and every budget hit is reported in the result so the
/// scoring layer can lower confidence instead of guessing.
/// </summary>
public sealed class DirectoryFactsCollector
{
    // Windows cloud-file attribute bits that FileAttributes does not name
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes CloudMask = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None, // hidden/system entries still count as content
        ReturnSpecialDirectories = false,
    };

    private enum Abort { None, Budget, Early }

    private sealed class WalkState
    {
        public required FactsWalkOptions Opt { get; init; }
        public CancellationToken Ct { get; init; }
        public int Entries;
        public Abort Reason = Abort.None;
    }

    public DirectoryFacts Collect(string path, FactsWalkOptions options, CancellationToken cancellationToken = default)
    {
        var st = new WalkState { Opt = options, Ct = cancellationToken };
        return Walk(new DirectoryInfo(path), 0, st);
    }

    private static DirectoryFacts Walk(DirectoryInfo dir, int depth, WalkState st)
    {
        var o = st.Opt;
        var facts = new DirectoryFacts();
        facts.AbsorbTimes(dir.LastWriteTimeUtc, o.UseCreationTime ? dir.CreationTimeUtc : DateTime.MinValue, dir.LastAccessTimeUtc, o.IncludeAccessTime);

        if (depth >= o.MaxDepth)
        {
            facts.Truncated = true;
            return facts;
        }

        List<DirectoryInfo>? subdirs = null;
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", Options))
            {
                if (++st.Entries > o.MaxEntries)
                {
                    st.Reason = Abort.Budget;
                    break;
                }
                if ((st.Entries & 0xFF) == 0)
                {
                    st.Ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > o.DeadlineUtc)
                    {
                        st.Reason = Abort.Budget;
                        break;
                    }
                }

                var attrs = entry.Attributes;
                bool isDir = (attrs & FileAttributes.Directory) != 0;
                bool isReparse = (attrs & FileAttributes.ReparsePoint) != 0;
                facts.OwnMarkers |= ProjectMarkerProbe.FromEntryName(entry.Name, isDir);

                if (isDir)
                {
                    if (isReparse)
                        facts.ReparsePointCount++; // never follow junctions / symlinks
                    else if (entry is DirectoryInfo di)
                        (subdirs ??= new List<DirectoryInfo>()).Add(di);
                    continue;
                }

                bool cloud = (attrs & CloudMask) != 0;
                if (isReparse && !cloud)
                {
                    facts.ReparsePointCount++; // file symlink
                    continue;
                }
                if (cloud)
                    facts.CloudPlaceholderCount++;

                long length = entry is FileInfo fi ? SafeLength(fi) : 0;
                facts.AddFile(FileClassifier.Classify(entry.Name), length);
                facts.AbsorbTimes(entry.LastWriteTimeUtc, o.UseCreationTime ? entry.CreationTimeUtc : DateTime.MinValue, entry.LastAccessTimeUtc, o.IncludeAccessTime);

                if (o.StopWhenActivityAfterUtc is { } cutoff && facts.NewestActivityUtc > cutoff)
                {
                    st.Reason = Abort.Early;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            facts.UnreadableCount++;
        }

        if (subdirs != null && st.Reason == Abort.None)
        {
            foreach (var sub in subdirs)
            {
                DirectoryFacts child;
                if (o.Memo != null && o.Memo.TryGet(sub.FullName, out var memoized))
                {
                    child = memoized;
                }
                else
                {
                    child = Walk(sub, depth + 1, st);
                    if (o.Memo != null && child.IsComplete && st.Reason == Abort.None && o.Memo.IsTarget(sub.FullName))
                        o.Memo.Publish(sub.FullName, child);
                }

                facts.Merge(child);

                if (st.Reason == Abort.None && o.StopWhenActivityAfterUtc is { } cutoff && facts.NewestActivityUtc > cutoff)
                    st.Reason = Abort.Early;
                if (st.Reason != Abort.None)
                    break;
            }
        }

        facts.SubtreeMarkers |= facts.OwnMarkers;
        if (st.Reason == Abort.Budget) facts.Truncated = true;
        if (st.Reason == Abort.Early) facts.StoppedEarly = true;
        return facts;
    }

    private static long SafeLength(FileInfo fi)
    {
        try { return fi.Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
