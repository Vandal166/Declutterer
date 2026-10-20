using Declutterer.Domain.Services.Cleanup;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Cleanup;

public class DirectoryFactsCollectorTests : IDisposable
{
    private readonly TempTestDirectory _t = new();
    private readonly DirectoryFactsCollector _c = new();

    public void Dispose() => _t.Dispose();

    private DirectoryFacts Collect(string rel = "", FactsWalkOptions? o = null) =>
        _c.Collect(string.IsNullOrEmpty(rel) ? _t.Path : Path.Combine(_t.Path, rel), o ?? new FactsWalkOptions { UseCreationTime = false });

    [Fact]
    public void CountsFilesBytesAndClasses_AcrossTheWholeSubtree()
    {
        _t.CreateFile("a.docx", 1000);
        _t.CreateFile("sub/b.jpg", 2000);
        _t.CreateFile("sub/deeper/c.tmp", 300);
        _t.CreateFile("sub/deeper/d.log", 40);

        var f = Collect();

        Assert.Equal(4, f.FileCount);
        Assert.Equal(3340, f.TotalBytes);
        Assert.Equal(2, f.DirectoryCount);
        Assert.Equal(1, f.CountByClass[(int)FileClass.Document]);
        Assert.Equal(2000, f.BytesByClass[(int)FileClass.Image]);
        Assert.True(f.IsComplete);
        Assert.False(f.IsEmpty);
    }

    [Fact]
    public void EmptyTree_IsEmpty_AndNewestActivityIsTheDirectoriesOwnTime()
    {
        _t.CreateSubDirectory("empty/inner");
        var f = Collect("empty");

        Assert.True(f.IsEmpty);
        Assert.Equal(1, f.DirectoryCount);
        Assert.True(f.NewestActivityUtc > DateTime.MinValue);
    }

    [Fact]
    public void NewestActivity_IsTheNewestFileAnywhereInTheTree()
    {
        _t.CreateFile("old.dat", 10);
        _t.CreateFile("deep/er/new.dat", 10);
        _t.SetLastModified("old.dat", DateTime.UtcNow.AddYears(-3));
        _t.SetLastModified("deep/er/new.dat", DateTime.UtcNow.AddDays(-2));
        _t.SetLastModified("deep/er", DateTime.UtcNow.AddYears(-3));
        _t.SetLastModified("deep", DateTime.UtcNow.AddYears(-3));
        _t.SetLastModified("", DateTime.UtcNow.AddYears(-3));

        var f = Collect();

        Assert.True((DateTime.UtcNow - f.NewestActivityUtc).TotalDays < 3);
    }

    [Fact]
    public void ProjectMarkers_AreCollected_OwnVersusSubtree()
    {
        _t.CreateFile("repo/package.json", 5);
        _t.CreateFile("repo/sub/.git/HEAD", 5);
        _t.CreateFile("repo/sub/app.csproj", 5);

        var f = Collect("repo");

        Assert.True(f.OwnMarkers.HasFlag(ProjectMarkers.NodePackage));
        Assert.False(f.OwnMarkers.HasFlag(ProjectMarkers.Git));
        Assert.True(f.SubtreeMarkers.HasFlag(ProjectMarkers.Git));
        Assert.True(f.SubtreeMarkers.HasFlag(ProjectMarkers.DotNetProject));
    }

    [Fact]
    public void Credentials_AreDetected()
    {
        _t.CreateFile("keys/deploy.pfx", 5);

        Assert.True(Collect("keys").HasCredentialFile);
    }

    [Fact]
    public void EntryBudget_TruncatesAndSaysSo()
    {
        for (int i = 0; i < 50; i++)
            _t.CreateFile($"many/f{i}.dat", 10);

        var f = Collect("many", new FactsWalkOptions { MaxEntries = 10, UseCreationTime = false });

        Assert.True(f.Truncated);
        Assert.False(f.IsComplete);
        Assert.True(f.FileCount <= 10);
    }

    [Fact]
    public void DepthBudget_Truncates()
    {
        _t.CreateFile("a/b/c/d/e.dat", 10);

        var f = Collect("a", new FactsWalkOptions { MaxDepth = 2, UseCreationTime = false });

        Assert.True(f.Truncated);
    }

    [Fact]
    public void ExpiredDeadline_Truncates_InsteadOfRunningOn()
    {
        for (int i = 0; i < 600; i++)
            _t.CreateFile($"big/f{i}.dat", 1);

        var f = Collect("big", new FactsWalkOptions { DeadlineUtc = DateTime.UtcNow.AddMinutes(-1), UseCreationTime = false });

        Assert.True(f.Truncated);
        Assert.True(f.FileCount < 600);
    }

    [Fact]
    public void ProvenAliveDirectory_StopsEarly_AndSaysSo()
    {
        for (int i = 0; i < 100; i++)
            _t.CreateFile($"alive/f{i}.dat", 1);

        var f = Collect("alive", new FactsWalkOptions
        {
            UseCreationTime = false,
            StopWhenActivityAfterUtc = DateTime.UtcNow.AddDays(-14),
        });

        Assert.True(f.StoppedEarly);
        Assert.False(f.IsComplete);
        Assert.True(f.FileCount < 100);
    }

    [Fact]
    public void SymbolicLinks_AreNeverFollowed()
    {
        _t.CreateFile("outside/secret.dat", 500);
        var holder = _t.CreateSubDirectory("holder");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(holder, "link"), Path.Combine(_t.Path, "outside"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // no privilege to create symlinks on this machine
        }

        var f = Collect("holder");

        Assert.Equal(0, f.FileCount);
        Assert.Equal(1, f.ReparsePointCount);
    }

    [Fact]
    public void CompletedTargetsAreMemoised_AndReusedByParents()
    {
        _t.CreateFile("parent/child/a.dat", 100);
        _t.CreateFile("parent/b.dat", 50);
        var childPath = Path.Combine(_t.Path, "parent", "child");
        var parentPath = Path.Combine(_t.Path, "parent");
        var memo = new FactsMemo(new[] { childPath, parentPath });
        var opt = new FactsWalkOptions { Memo = memo, UseCreationTime = false };

        var child = _c.Collect(childPath, opt);
        memo.Publish(childPath, child);
        Assert.True(memo.TryGet(childPath, out _));

        // delete the child's content on disk: if the parent walk re-enumerated it the bytes would be 50, not 150
        File.Delete(Path.Combine(childPath, "a.dat"));
        var parent = _c.Collect(parentPath, opt);

        Assert.Equal(150, parent.TotalBytes);
    }

    [Fact]
    public void ValuableShare_IgnoresBundledAssetsInsideAppData()
    {
        _t.CreateFile("app/icon1.png", 10);
        _t.CreateFile("app/icon2.png", 10);
        _t.CreateFile("app/ui.js", 10);
        _t.CreateFile("app/readme.docx", 10);

        var f = Collect("app");

        Assert.True(f.ValuableShare(assetsAreNoise: false) > 0.99);
        Assert.True(f.ValuableShare(assetsAreNoise: true) < 0.4);
    }
}

public class FileClassifierTests
{
    [Theory]
    [InlineData("report.DOCX", FileClass.Document)]
    [InlineData("photo.jpeg", FileClass.Image)]
    [InlineData("a.tmp", FileClass.Temp)]
    [InlineData("~$report.docx", FileClass.Temp)]
    [InlineData("Thumbs.db", FileClass.Temp)]
    [InlineData("crash.dmp", FileClass.Dump)]
    [InlineData("setup.msi", FileClass.Installer)]
    [InlineData("id_rsa", FileClass.Credential)]
    [InlineData("vault.kdbx", FileClass.Database)]
    [InlineData("deploy.pfx", FileClass.Credential)]
    [InlineData("noextension", FileClass.Unknown)]
    [InlineData("weird.zzz", FileClass.Unknown)]
    public void Classify_UsesNameAndExtension(string name, FileClass expected)
    {
        Assert.Equal(expected, FileClassifier.Classify(name));
    }

    [Fact]
    public void ValuableAndDisposable_AreDisjoint()
    {
        foreach (var c in Enum.GetValues<FileClass>())
            Assert.False(FileClassifier.IsValuable(c) && FileClassifier.IsDisposable(c), c.ToString());
    }
}
