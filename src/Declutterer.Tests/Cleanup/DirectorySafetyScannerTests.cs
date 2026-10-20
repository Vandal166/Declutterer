using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Models;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Cleanup;

public sealed class DirectorySafetyScannerTests : IDisposable
{
    private readonly TempTestDirectory _temp = new();
    private readonly DirectorySafetyScanner _scanner = new();
    private readonly SystemSnapshot _machine = new(new SystemSnapshotData());

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ReparsePointBlocksWholeDirectoryAndIsNotFollowed()
    {
        var root = Path.Combine(_temp.Path, "candidate");
        var target = Path.Combine(_temp.Path, "outside");
        var link = Path.Combine(root, "redirect");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "outside.txt"), "target");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
        {
            return; // Symlink creation requires a Windows privilege that may be disabled on a developer machine.
        }

        var result = Scan(root);

        Assert.Equal(DirectorySafetyStatus.Blocked, result.Status);
        Assert.Contains("reparse point", result.BlockReason);
        Assert.Equal(1, result.EntriesVisited);
    }

    [Fact]
    public void DepthLimitMakesSafetyScanIncomplete()
    {
        var root = Path.Combine(_temp.Path, "candidate");
        Directory.CreateDirectory(Path.Combine(root, "one", "two"));
        File.WriteAllText(Path.Combine(root, "one", "two", "a.tmp"), "data");

        var result = Scan(root, new SmartCleanupOptions { MaxDepth = 1 });

        Assert.Equal(DirectorySafetyStatus.Incomplete, result.Status);
        Assert.Contains("depth limit", result.BlockReason);
    }

    [Fact]
    public void RecentActivityDoesNotStopTheSafetyWalkEarly()
    {
        var root = Path.Combine(_temp.Path, "candidate");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        var recent = Path.Combine(root, "nested", "recent.txt");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow);

        var result = Scan(root);

        Assert.Equal(DirectorySafetyStatus.Safe, result.Status);
        Assert.Equal(2, result.EntriesVisited);
    }

    [Fact]
    public void ExpiredDeadlineMakesSafetyScanIncomplete()
    {
        var root = Path.Combine(_temp.Path, "candidate");
        Directory.CreateDirectory(root);

        var result = _scanner.Scan(root, _machine, new SmartCleanupOptions(), DateTime.UtcNow.AddSeconds(-1),
            JunkAssessmentEngine.DefaultSafetyGate);

        Assert.Equal(DirectorySafetyStatus.Incomplete, result.Status);
        Assert.True(result.BudgetExceeded);
    }

    private DirectorySafetyScanResult Scan(string root, SmartCleanupOptions? options = null) =>
        _scanner.Scan(root, _machine, options ?? new SmartCleanupOptions(), DateTime.UtcNow.AddMinutes(1),
            JunkAssessmentEngine.DefaultSafetyGate);
}
