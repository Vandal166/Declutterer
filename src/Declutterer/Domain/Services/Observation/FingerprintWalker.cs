using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Declutterer.Domain.Services.Observation;

public sealed class FingerprintWalkOptions
{
    public int MaxEntries { get; init; } = 300_000;
    public int MaxDepth { get; init; } = 48;
    public DateTime DeadlineUtc { get; init; } = DateTime.MaxValue;
    public int ChildHashCap { get; init; } = 2000;
    /// <summary>Files accessed after this time are counted in <see cref="Fingerprint.FilesAccessedSince"/>.</summary>
    public DateTime? AccessedSinceUtc { get; init; }
    public int MaxAccessSamples { get; init; } = 200;
}

/// <summary>
/// Walks a tree reading directory entries and their metadata only: it never opens a file, so it can not change a last-access
/// time of a file or hydrate a cloud placeholder. Reparse points (junctions, symlinks) are never followed. Every budget hit or
/// unreadable directory is reported through <see cref="Fingerprint.IsComplete"/>.
/// </summary>
public sealed class FingerprintWalker
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = false, // an unreadable entry must make the walk incomplete, not silently shorter
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        ReturnSpecialDirectories = false,
    };

    private static readonly HashSet<string> ShellDroppings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db", "desktop.ini", ".DS_Store",
    };

    private readonly TimeProvider _time;

    public FingerprintWalker(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    public static bool IsShellDropping(string fileName) => ShellDroppings.Contains(fileName);

    private sealed class State
    {
        public required FingerprintWalkOptions Opt { get; init; }
        public required IncrementalHash Root { get; init; }
        public required IncrementalHash Sig { get; init; }
        public required IncrementalHash Drop { get; init; }
        public CancellationToken Ct { get; init; }
        public DateTime Now { get; init; }
        public int Entries;
        public int Files;
        public long Bytes;
        public DateTime NewestWrite = DateTime.MinValue;
        public DateTime NewestAccess = DateTime.MinValue;
        public int AccessedSince;
        public int Reparse;
        public int Unreadable;
        public bool Truncated;
        public bool ChildrenTruncated;
        public readonly Dictionary<string, string> Children = new();
        public readonly List<AccessSample> Samples = new();
    }

    public Fingerprint Walk(string path, FingerprintWalkOptions? options = null, CancellationToken cancellationToken = default)
    {
        var opt = options ?? new FingerprintWalkOptions();
        var now = _time.GetUtcNow().UtcDateTime;

        using var root = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sig = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var drop = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var st = new State { Opt = opt, Root = root, Sig = sig, Drop = drop, Ct = cancellationToken, Now = now };

        try
        {
            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                Append(st, null, string.Empty, dir.Name, 'D', 0, SafeWrite(dir));
                WalkDirectory(st, dir, string.Empty, 0, null);
            }
            else if (File.Exists(path))
            {
                var file = new FileInfo(path);
                AddFile(st, file, string.Empty, null);
            }
            else
            {
                st.Unreadable++; // nothing there: the caller checks existence first and treats this as "no usable fingerprint"
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            st.Unreadable++;
        }

        return new Fingerprint
        {
            RootHash = Hex(root.GetHashAndReset()),
            SignificantHash = Hex(sig.GetHashAndReset()),
            DroppingHash = Hex(drop.GetHashAndReset()),
            Children = st.Children,
            ChildrenTruncated = st.ChildrenTruncated,
            EntryCount = st.Entries,
            TotalBytes = st.Bytes,
            FileCount = st.Files,
            NewestWriteUtc = st.NewestWrite,
            NewestAccessUtc = st.NewestAccess,
            FilesAccessedSince = st.AccessedSince,
            ReparsePointCount = st.Reparse,
            UnreadableCount = st.Unreadable,
            Truncated = st.Truncated,
            CapturedUtc = now,
            AccessSamples = st.Samples,
        };
    }

    private void WalkDirectory(State st, DirectoryInfo dir, string prefix, int depth, IncrementalHash? child)
    {
        if (depth >= st.Opt.MaxDepth)
        {
            st.Truncated = true;
            return;
        }

        var entries = new List<FileSystemInfo>();
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", Enumeration))
            {
                st.Ct.ThrowIfCancellationRequested();
                if (st.Entries >= st.Opt.MaxEntries)
                {
                    st.Truncated = true;
                    return;
                }
                if ((st.Entries & 0xFF) == 0 && _time.GetUtcNow().UtcDateTime >= st.Opt.DeadlineUtc)
                {
                    st.Truncated = true;
                    return;
                }

                entries.Add(entry);
                st.Entries++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            st.Unreadable++;
            return;
        }
        entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        int processed = 0;
        foreach (var entry in entries)
        {
            st.Ct.ThrowIfCancellationRequested();
            if ((processed++ & 0xFF) == 0 && _time.GetUtcNow().UtcDateTime >= st.Opt.DeadlineUtc)
            {
                st.Truncated = true;
                return;
            }

            FileAttributes attributes;
            try
            {
                attributes = entry.Attributes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                st.Unreadable++;
                continue;
            }

            var rel = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            bool isReparse = (attributes & FileAttributes.ReparsePoint) != 0;

            // first-level entries get their own hash (capped): it is what lets a change be attributed without a second walk
            IncrementalHash? own = child;
            bool disposeOwn = false;
            if (depth == 0)
            {
                if (st.Children.Count < st.Opt.ChildHashCap)
                {
                    own = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    disposeOwn = true;
                }
                else
                {
                    st.ChildrenTruncated = true;
                    own = null;
                }
            }

            try
            {
                if (isReparse)
                {
                    st.Reparse++;
                    Append(st, own, rel, entry.Name, 'L', 0, SafeWrite(entry)); // recorded, never followed
                }
                else if (isDirectory)
                {
                    Append(st, own, rel, entry.Name, 'D', 0, SafeWrite(entry));
                    if (entry is DirectoryInfo sub)
                        WalkDirectory(st, sub, rel, depth + 1, own);
                }
                else if (entry is FileInfo file)
                {
                    AddFile(st, file, rel, own);
                }

                if (depth == 0 && own is not null && !st.Truncated)
                    st.Children[entry.Name] = Hex(own.GetHashAndReset()).Substring(0, 16);
            }
            finally
            {
                if (disposeOwn)
                    own!.Dispose();
            }

            if (st.Truncated)
                return;
        }
    }

    private static void AddFile(State st, FileInfo file, string rel, IncrementalHash? child)
    {
        long length;
        try
        {
            length = file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            length = 0;
            st.Unreadable++;
        }

        var write = SafeWrite(file);
        Append(st, child, rel, file.Name, 'F', length, write);

        st.Files++;
        st.Bytes += length;

        // access times: files only (a directory's own access time is changed by merely listing it)
        DateTime access;
        try
        {
            access = file.LastAccessTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (access > st.NewestAccess)
            st.NewestAccess = access;
        if (st.Opt.AccessedSinceUtc is { } since && access > since)
            st.AccessedSince++;
        if (st.Samples.Count < st.Opt.MaxAccessSamples)
        {
            DateTime created;
            try { created = file.CreationTimeUtc; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            st.Samples.Add(new AccessSample(access, write, created));
        }
    }

    private static DateTime SafeWrite(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Feeds one entry into the root hash, the "significant" hash and (for shell droppings) the dropping hash.</summary>
    private static void Append(State st, IncrementalHash? child, string rel, string name, char kind, long size, DateTime write)
    {
        if (write > st.NewestWrite && kind == 'F')
            st.NewestWrite = write;

        var full = Encode(rel, kind, size, write.Ticks);
        st.Root.AppendData(full);
        child?.AppendData(full);

        if (kind == 'F' && ShellDroppings.Contains(name))
        {
            st.Drop.AppendData(full);
        }
        else
        {
            // directory times are left out of the significant hash: creating a Thumbs.db changes its folder's time
            st.Sig.AppendData(kind == 'F' ? full : Encode(rel, kind, 0, 0));
        }
    }

    private static byte[] Encode(string rel, char kind, long size, long ticks)
    {
        var name = Encoding.UTF8.GetBytes(rel);
        var buffer = new byte[name.Length + 20];
        name.CopyTo(buffer, 0);
        int p = name.Length;
        buffer[p++] = 0;
        buffer[p++] = (byte)kind;
        buffer[p++] = 0;
        BitConverter.TryWriteBytes(buffer.AsSpan(p, 8), size);
        p += 8;
        BitConverter.TryWriteBytes(buffer.AsSpan(p, 8), ticks);
        p += 8;
        buffer[p] = (byte)'\n';
        return buffer;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
