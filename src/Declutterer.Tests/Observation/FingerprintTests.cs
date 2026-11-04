using System.Security.Cryptography;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Observation;

public class FingerprintTests : IDisposable
{
    private readonly TempTestDirectory _t = new();
    private readonly FingerprintWalker _walker = new();

    public void Dispose() => _t.Dispose();

    private Fingerprint Walk(string rel = "", FingerprintWalkOptions? o = null) =>
        _walker.Walk(string.IsNullOrEmpty(rel) ? _t.Path : Path.Combine(_t.Path, rel), o);

    /// <summary>Gives every file and folder a fixed, distinct time so nothing depends on how fast the test runs.</summary>
    private void Settle()
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var f in Directory.EnumerateFiles(_t.Path, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(f, t);
        foreach (var d in Directory.EnumerateDirectories(_t.Path, "*", SearchOption.AllDirectories))
            Directory.SetLastWriteTimeUtc(d, t);
        Directory.SetLastWriteTimeUtc(_t.Path, t);
    }

    [Fact]
    public void SameTree_GivesTheSameFingerprint()
    {
        _t.CreateFile("a/one.dat", 100);
        _t.CreateFile("a/b/two.dat", 200);
        _t.CreateFile("three.dat", 300);
        Settle();

        var first = Walk();
        var second = Walk();

        Assert.True(first.IsComplete);
        Assert.Equal(first.RootHash, second.RootHash);
        Assert.Equal(first.Children, second.Children);
        Assert.Equal(3, first.FileCount);
        Assert.Equal(600, first.TotalBytes);
        Assert.True(FingerprintComparer.Compare(first, second).Identical);
    }

    [Fact]
    public void CreatedModifiedAndDeletedEntries_AreCountedAtTheFirstLevel()
    {
        _t.CreateFile("keep/k.dat", 10);
        _t.CreateFile("edit/e.dat", 10);
        _t.CreateFile("remove/r.dat", 10);
        Settle();
        var before = Walk();

        _t.CreateFile("added/a.dat", 10);
        File.WriteAllText(Path.Combine(_t.Path, "edit", "e.dat"), "now longer content");
        Directory.Delete(Path.Combine(_t.Path, "remove"), recursive: true);
        var after = Walk();
        var report = FingerprintComparer.Compare(before, after);

        Assert.False(report.Identical);
        Assert.False(report.ShellDroppingsOnly);
        Assert.Equal(1, report.Created);
        Assert.Equal(1, report.Modified);
        Assert.Equal(1, report.Deleted);
        Assert.Contains("1 created", report.Describe());
    }

    [Fact]
    public void ChangeDeepInsideATree_IsSeenThroughTheFirstLevelChild()
    {
        _t.CreateFile("top/mid/deep/leaf.dat", 10);
        Settle();
        var before = Walk();

        File.AppendAllText(Path.Combine(_t.Path, "top", "mid", "deep", "leaf.dat"), "x");
        var report = FingerprintComparer.Compare(before, Walk());

        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.Modified);
    }

    [Fact]
    public void OnlyShellDroppings_AreRecognisedAtAnyDepth()
    {
        _t.CreateFile("pics/a.jpg", 10);
        _t.CreateFile("pics/Thumbs.db", 10);
        Settle();
        var before = Walk();

        File.WriteAllText(Path.Combine(_t.Path, "pics", "Thumbs.db"), "rewritten by explorer");
        File.WriteAllText(Path.Combine(_t.Path, "pics", "desktop.ini"), "[.ShellClassInfo]");
        Directory.SetLastWriteTimeUtc(Path.Combine(_t.Path, "pics"), DateTime.UtcNow);
        var report = FingerprintComparer.Compare(before, Walk());

        Assert.False(report.Identical);
        Assert.True(report.ShellDroppingsOnly, report.Describe());
    }

    [Fact]
    public void ARealChangeNextToShellDroppings_IsNotShellOnly()
    {
        _t.CreateFile("pics/a.jpg", 10);
        Settle();
        var before = Walk();

        File.WriteAllText(Path.Combine(_t.Path, "pics", "Thumbs.db"), "x");
        File.WriteAllText(Path.Combine(_t.Path, "pics", "b.jpg"), "a new picture");

        Assert.False(FingerprintComparer.Compare(before, Walk()).ShellDroppingsOnly);
    }

    [Fact]
    public void ADirectoryTimeChangeAlone_IsARealChange_NotShellDroppings()
    {
        _t.CreateFile("work/a.dat", 10);
        Settle();
        var before = Walk();

        // something created and removed a file: no entry differs, only the folder's own time
        Directory.SetLastWriteTimeUtc(Path.Combine(_t.Path, "work"), new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var report = FingerprintComparer.Compare(before, Walk());

        Assert.False(report.Identical);
        Assert.False(report.ShellDroppingsOnly);
        Assert.True(report.Total > 0);
    }

    [Fact]
    public void RenamingTheRootDirectory_DoesNotChangeTheFingerprint()
    {
        _t.CreateFile("item/a.dat", 10);
        _t.CreateFile("item/sub/b.dat", 20);
        Settle();
        var before = Walk("item");

        Directory.Move(Path.Combine(_t.Path, "item"), Path.Combine(_t.Path, "renamed"));
        var after = Walk("renamed");

        Assert.True(FingerprintComparer.Compare(before, after).Identical);
    }

    [Fact]
    public void EntryBudget_MakesTheFingerprintIncomplete_AndNotComparable()
    {
        for (int i = 0; i < 40; i++)
            _t.CreateFile($"many/f{i}.dat", 10);
        Settle();

        var truncated = Walk("many", new FingerprintWalkOptions { MaxEntries = 10 });
        var full = Walk("many");

        Assert.True(truncated.Truncated);
        Assert.False(truncated.IsComplete);
        Assert.False(FingerprintComparer.Compare(full, truncated).Comparable);
        Assert.False(FingerprintComparer.Compare(truncated, truncated).Comparable); // incomplete never proves "unchanged"
    }

    [Fact]
    public void DepthBudget_MakesTheFingerprintIncomplete()
    {
        _t.CreateFile("a/b/c/d/e.dat", 10);

        Assert.False(Walk("a", new FingerprintWalkOptions { MaxDepth = 2 }).IsComplete);
    }

    [Fact]
    public void ExpiredDeadline_Truncates()
    {
        for (int i = 0; i < 600; i++)
            _t.CreateFile($"big/f{i}.dat", 1);

        var fp = Walk("big", new FingerprintWalkOptions { DeadlineUtc = DateTime.UtcNow.AddMinutes(-1) });

        Assert.True(fp.Truncated);
        Assert.False(fp.IsComplete);
    }

    [Fact]
    public void Cancellation_Throws()
    {
        for (int i = 0; i < 20; i++)
            _t.CreateFile($"c/f{i}.dat", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _walker.Walk(Path.Combine(_t.Path, "c"), null, cts.Token));
    }

    [Fact]
    public void MissingPath_IsNotACompleteFingerprint()
    {
        var fp = _walker.Walk(Path.Combine(_t.Path, "nope"));

        Assert.False(fp.IsComplete);
    }

    [Fact]
    public void SingleFile_IsFingerprinted()
    {
        var file = _t.CreateFile("one.bin", 123);
        File.SetLastWriteTimeUtc(file, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = _walker.Walk(file);

        File.AppendAllText(file, "more");
        var after = _walker.Walk(file);

        Assert.True(before.IsComplete);
        Assert.Equal(123, before.TotalBytes);
        Assert.False(FingerprintComparer.Compare(before, after).Identical);
    }

    [Fact]
    public void ChildHashCap_IsReportedAndStillComparable()
    {
        for (int i = 0; i < 12; i++)
            _t.CreateFile($"c{i}/x.dat", 10);
        Settle();

        var capped = Walk("", new FingerprintWalkOptions { ChildHashCap = 5 });

        Assert.Equal(5, capped.Children.Count);
        Assert.True(capped.ChildrenTruncated);
        Assert.True(capped.IsComplete);
        Assert.True(FingerprintComparer.Compare(capped, Walk("", new FingerprintWalkOptions { ChildHashCap = 5 })).Identical);
    }

    [Fact]
    public void ReparsePoints_AreRecordedButNeverFollowed()
    {
        _t.CreateFile("outside/secret.dat", 500);
        var holder = _t.CreateSubDirectory("holder");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(holder, "link"), Path.Combine(_t.Path, "outside"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // no privilege to create symbolic links on this machine
        }

        var fp = Walk("holder");

        Assert.Equal(1, fp.ReparsePointCount);
        Assert.Equal(0, fp.FileCount);
        Assert.Equal(0, fp.TotalBytes);
    }

    [Fact]
    public void Walking_NeverReadsOrChangesAnything_Invariant1()
    {
        var path = _t.CreateFile("data/payload.bin", 4096);
        new Random(3).NextBytes(new byte[1]);
        File.WriteAllBytes(path, Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray());
        var old = new DateTime(2023, 5, 5, 5, 5, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, old);
        File.SetLastAccessTimeUtc(path, old);

        var contentBefore = SHA256.HashData(File.ReadAllBytes(path));
        // re-pin the access time: ReadAllBytes above is the test's own read
        File.SetLastAccessTimeUtc(path, old);
        var infoBefore = new FileInfo(path);
        var (len, write, access) = (infoBefore.Length, infoBefore.LastWriteTimeUtc, infoBefore.LastAccessTimeUtc);

        _walker.Walk(Path.Combine(_t.Path, "data"), new FingerprintWalkOptions { AccessedSinceUtc = old.AddDays(-1) });
        _walker.Walk(Path.Combine(_t.Path, "data"));

        var infoAfter = new FileInfo(path);
        Assert.Equal(len, infoAfter.Length);
        Assert.Equal(write, infoAfter.LastWriteTimeUtc);
        Assert.Equal(access, infoAfter.LastAccessTimeUtc); // listing metadata did not touch the file
        Assert.Equal(contentBefore, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public void AccessStatistics_CountOnlyFiles_AndSampleThem()
    {
        _t.CreateFile("d/old.dat", 10);
        _t.CreateFile("d/new.dat", 10);
        var oldT = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newT = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_t.Path, "d", "old.dat"), oldT);
        File.SetLastWriteTimeUtc(Path.Combine(_t.Path, "d", "new.dat"), oldT);
        File.SetLastAccessTimeUtc(Path.Combine(_t.Path, "d", "old.dat"), oldT);
        File.SetLastAccessTimeUtc(Path.Combine(_t.Path, "d", "new.dat"), newT);

        var fp = Walk("d", new FingerprintWalkOptions { AccessedSinceUtc = oldT.AddDays(30) });

        Assert.Equal(newT, fp.NewestAccessUtc);
        Assert.Equal(1, fp.FilesAccessedSince);
        Assert.Equal(2, fp.AccessSamples.Count);
    }

    [Fact]
    public void Json_RoundTripsEverythingThatIsPersisted_AndGarbageIsNotAFingerprint()
    {
        _t.CreateFile("x/a.dat", 10);
        Settle();
        var fp = Walk();

        var back = Fingerprint.FromJson(fp.ToJson());

        Assert.NotNull(back);
        Assert.Equal(fp.RootHash, back!.RootHash);
        Assert.Equal(fp.SignificantHash, back.SignificantHash);
        Assert.Equal(fp.DroppingHash, back.DroppingHash);
        Assert.Equal(fp.Children, back.Children);
        Assert.Equal(fp.CapturedUtc, back.CapturedUtc);
        Assert.True(back.IsComplete);
        Assert.Null(Fingerprint.FromJson("{ not json"));
        Assert.Null(Fingerprint.FromJson(null));
        Assert.Empty(back.AccessSamples); // samples are never persisted
    }
}
