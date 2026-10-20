using System;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// Aggregated, read-only-after-construction facts about a directory subtree (or a single file).
/// Produced by <see cref="DirectoryFactsCollector"/> and consumed by the assessment rules.
/// </summary>
public sealed class DirectoryFacts
{
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
    public int DirectoryCount { get; set; }

    /// <summary>Newest of last-write / creation (/ last-access when reliable) over the whole subtree.</summary>
    public DateTime NewestActivityUtc { get; set; } = DateTime.MinValue;
    public DateTime NewestWriteUtc { get; set; } = DateTime.MinValue;
    public DateTime NewestAccessUtc { get; set; } = DateTime.MinValue;

    public long[] BytesByClass { get; } = new long[FileClassifier.ClassCount];
    public int[] CountByClass { get; } = new int[FileClassifier.ClassCount];

    /// <summary>Markers directly inside the directory itself (not merged from children).</summary>
    public ProjectMarkers OwnMarkers { get; set; }

    /// <summary>Union of markers of the whole subtree.</summary>
    public ProjectMarkers SubtreeMarkers { get; set; }

    public bool HasCredentialFile => CountByClass[(int)FileClass.Credential] > 0;
    public int ReparsePointCount { get; set; }
    public int CloudPlaceholderCount { get; set; }
    public int UnreadableCount { get; set; }

    /// <summary>The walk hit a budget (entries / depth / time). Facts are a lower bound.</summary>
    public bool Truncated { get; set; }

    /// <summary>The walk stopped because recent activity proved the directory is alive. Facts are partial by design.</summary>
    public bool StoppedEarly { get; set; }

    public bool IsComplete => !Truncated && !StoppedEarly;

    /// <summary>No files anywhere below (only meaningful for complete facts).</summary>
    public bool IsEmpty => FileCount == 0 && ReparsePointCount == 0 && UnreadableCount == 0;

    public void AbsorbTimes(DateTime writeUtc, DateTime creationUtc, DateTime accessUtc, bool useAccess)
    {
        if (writeUtc > NewestWriteUtc) NewestWriteUtc = writeUtc;
        var activity = writeUtc > creationUtc ? writeUtc : creationUtc;
        if (useAccess && accessUtc > activity) activity = accessUtc;
        if (accessUtc > NewestAccessUtc) NewestAccessUtc = accessUtc;
        if (activity > NewestActivityUtc) NewestActivityUtc = activity;
    }

    public void AddFile(FileClass cls, long length)
    {
        FileCount++;
        TotalBytes += length;
        BytesByClass[(int)cls] += length;
        CountByClass[(int)cls]++;
    }

    /// <summary>Folds a child directory's facts into this one (the child object is not modified).</summary>
    public void Merge(DirectoryFacts child)
    {
        TotalBytes += child.TotalBytes;
        FileCount += child.FileCount;
        DirectoryCount += child.DirectoryCount + 1;
        if (child.NewestActivityUtc > NewestActivityUtc) NewestActivityUtc = child.NewestActivityUtc;
        if (child.NewestWriteUtc > NewestWriteUtc) NewestWriteUtc = child.NewestWriteUtc;
        if (child.NewestAccessUtc > NewestAccessUtc) NewestAccessUtc = child.NewestAccessUtc;
        for (int i = 0; i < BytesByClass.Length; i++)
        {
            BytesByClass[i] += child.BytesByClass[i];
            CountByClass[i] += child.CountByClass[i];
        }
        SubtreeMarkers |= child.SubtreeMarkers;
        ReparsePointCount += child.ReparsePointCount;
        CloudPlaceholderCount += child.CloudPlaceholderCount;
        UnreadableCount += child.UnreadableCount;
        Truncated |= child.Truncated;
        StoppedEarly |= child.StoppedEarly;
    }

    /// <summary>Facts for a single file, so rules can treat files and directories uniformly.</summary>
    public static DirectoryFacts ForFile(FileClass cls, long length, DateTime writeUtc)
    {
        var f = new DirectoryFacts();
        f.AddFile(cls, length);
        f.NewestWriteUtc = f.NewestActivityUtc = writeUtc;
        return f;
    }

    /// <summary>
    /// Share (0..1) of the content that is valuable user data, by file count or by bytes (whichever is larger).
    /// With <paramref name="assetsAreNoise"/> media/source files only count a little (application data folders are full of bundled assets).
    /// </summary>
    public double ValuableShare(bool assetsAreNoise)
    {
        double count = 0, bytes = 0;
        for (int i = 0; i < CountByClass.Length; i++)
        {
            var cls = (FileClass)i;
            if (!FileClassifier.IsValuable(cls))
                continue;
            double w = assetsAreNoise && FileClassifier.IsAssetLike(cls) ? 0.15 : 1.0;
            count += CountByClass[i] * w;
            bytes += BytesByClass[i] * w;
        }

        double byCount = FileCount == 0 ? 0 : count / FileCount;
        double byBytes = TotalBytes == 0 ? 0 : bytes / TotalBytes;
        return Math.Max(byCount, byBytes);
    }

    /// <summary>Number of valuable files (weighted like <see cref="ValuableShare"/>).</summary>
    public double ValuableFileWeight(bool assetsAreNoise)
    {
        double count = 0;
        for (int i = 0; i < CountByClass.Length; i++)
        {
            var cls = (FileClass)i;
            if (FileClassifier.IsValuable(cls))
                count += CountByClass[i] * (assetsAreNoise && FileClassifier.IsAssetLike(cls) ? 0.15 : 1.0);
        }
        return count;
    }

    public double DisposableByteShare()
    {
        if (TotalBytes == 0)
            return 0;
        long b = 0;
        for (int i = 0; i < BytesByClass.Length; i++)
            if (FileClassifier.IsDisposable((FileClass)i))
                b += BytesByClass[i];
        return (double)b / TotalBytes;
    }

    public double ShareByCount(FileClass a, FileClass b)
    {
        if (FileCount == 0) return 0;
        return (CountByClass[(int)a] + CountByClass[(int)b]) / (double)FileCount;
    }
}
