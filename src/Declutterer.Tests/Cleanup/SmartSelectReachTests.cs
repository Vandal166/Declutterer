using System.Diagnostics;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Tests.Cleanup;

/// <summary>
/// Regression tests for the reasons Smart Select used to find very little on a big drive: slow lookups that blew the time
/// budget, time-outs that were remembered forever, safety scans that starved behind the disk inspection, and deepening
/// that gave up after the first slow pass.
/// </summary>
public class SmartSelectReachTests : IDisposable
{
    private readonly CleanupScenario _s = new();

    public void Dispose() => _s.Dispose();

    // ── installed-program lookups must not scale with the size of the inventory ────────────────────────

    [Fact]
    public void InstalledAppLookup_IsCorrectAndFast_WithAFullWindowsInventory()
    {
        var sep = Path.DirectorySeparatorChar;
        string Loc(string group, int i) => string.Join(sep, "", group, "app" + i);

        var data = new SystemSnapshotData { HasInstalledAppInventory = true };
        for (int i = 0; i < 400; i++)
            data.InstalledApps.Add(new InstalledApp("App " + i, "Vendor " + i, new[] { Loc("programs", i), Loc("tools", i), Loc("other", i) }));
        var snapshot = new SystemSnapshot(data);

        Assert.True(snapshot.TryGetInstalledAppAt(Loc("programs", 123) + sep + "bin" + sep + "x.dll", out var inside));
        Assert.Equal("App 123", inside);
        Assert.True(snapshot.TryGetInstalledAppAt(Loc("OTHER", 7), out var equal)); // case-insensitive
        Assert.Equal("App 7", equal);
        Assert.True(snapshot.TryGetInstalledAppAt(sep + "tools", out _));            // contains install locations
        Assert.True(snapshot.TryGetInstalledAppAt(sep.ToString(), out _));            // filesystem root contains install locations
        Assert.False(snapshot.TryGetInstalledAppAt(Loc("programs", 1000) + sep + "x", out _));
        Assert.False(snapshot.TryGetInstalledAppAt(Loc("programs", 12) + "x", out _)); // shares a prefix with app12, but is not inside it
    }

    [Fact]
    public void InstalledAppLookup_DoesNotAddUpPerFile()
    {
        var sep = Path.DirectorySeparatorChar;
        var data = new SystemSnapshotData { HasInstalledAppInventory = true };
        for (int i = 0; i < 1500; i++)
            data.InstalledApps.Add(new InstalledApp("App " + i, null, new[] { sep + "a" + sep + i, sep + "b" + sep + i, sep + "c" + sep + i }));
        var snapshot = new SystemSnapshot(data);
        var path = string.Join(sep, "", "users", "me", "projects", "proj", "node_modules", "pkg", "lib", "index.js");

        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++)
            snapshot.TryGetInstalledAppAt(path, out _);
        clock.Stop();

        // Scanning every location for every path took ~15 s for this (and grows with every installed program);
        // the ancestor lookup needs a fraction of a second.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"100k lookups took {clock.Elapsed}");
    }

    // ── a time-out must not be remembered ──────────────────────────────────────────────────────────────

    [Fact]
    public void Session_RemembersCompletedAndDeterministicScans_ButNotTimeOuts()
    {
        var session = new AnalysisSession();
        var timedOut = new DirectorySafetyScanResult(DirectorySafetyStatus.Incomplete, "time budget", 10, BudgetExceeded: true);
        var tooDeep = new DirectorySafetyScanResult(DirectorySafetyStatus.Incomplete, "depth limit", 10, BudgetExceeded: false);
        var clean = new DirectorySafetyScanResult(DirectorySafetyStatus.Safe, null, 10, BudgetExceeded: false);

        session.PublishSafety("/a", timedOut);
        session.PublishSafety("/b", tooDeep);
        session.PublishSafety("/c", clean);

        Assert.False(session.TryGetSafety("/a", out _));
        Assert.True(session.TryGetSafety("/b", out _));
        Assert.True(session.TryGetSafety("/c", out _));
    }

    [Fact]
    public void FolderThatTimedOutInOnePass_IsSelectable_InTheNextPassOfTheSameSession()
    {
        _s.Session = new AnalysisSession();
        _s.File("Projects/old-app/package.json", 800, 300);
        _s.File("Projects/old-app/node_modules/x/index.js", 800, 4000);
        const string nm = "Projects/old-app/node_modules";

        _s.Options.TimeBudget = TimeSpan.Zero;
        var starved = _s.Run();
        Assert.NotNull(starved[nm].SelectionBlockReason);
        Assert.DoesNotContain(nm, starved.Selected(SmartSelectMode.Aggressive));

        _s.Options.TimeBudget = TimeSpan.FromSeconds(30);
        var retried = _s.Run();

        Assert.Null(retried[nm].SelectionBlockReason);
        Assert.Equal(CleanupTier.Safe, retried.Tier(nm));
        Assert.Contains(nm, retried.Selected(SmartSelectMode.Conservative));
    }

    // ── safety scans have their own time, they do not queue up behind the disk inspection ──────────────

    private sealed class SlowFirstLookRule : IAssessmentRule
    {
        public void Evaluate(AssessmentContext ctx, SignalSink sink)
        {
            // stage A (no facts yet) of one folder: burns more than the whole time budget before the disk inspection starts
            if (ctx.Facts is null && ctx.IsDirectory && ctx.Name == "cache")
                Thread.Sleep(900);
        }
    }

    [Fact]
    public void SafetyScan_StillCompletes_WhenTheInspectionAlreadyUsedUpTheBudget()
    {
        _s.Options.TimeBudget = TimeSpan.FromMilliseconds(600);
        _s.Rules = JunkAssessmentEngine.DefaultRules().Append(new SlowFirstLookRule()).ToList();
        _s.File("AppData/Local/Temp/cache/old.tmp", 800, 2000);
        const string dir = "AppData/Local/Temp/cache";

        var r = _s.Run();

        Assert.True(r.Analysis.BudgetExceeded, "the inspection should have been starved in this scenario");
        Assert.True(r.Analysis.DirectoriesNotInspected > 0);
        Assert.True(r[dir].SelectionSafetyComplete, r.Explain(dir));
        Assert.Null(r[dir].SelectionBlockReason);
    }

    // ── what is opened first when looking inside folders ───────────────────────────────────────────────

    private static NodeSnapshot Folder(string name, NodeSnapshot parent, long size) =>
        new()
        {
            Path = parent.Path + "/" + name, Name = name, IsDirectory = true, Size = size, Depth = parent.Depth + 1, Parent = parent,
            HasUnloadedChildren = true, Source = new TreeNode { FullPath = "/x/" + name },
        };

    [Fact]
    public void Planner_OpensProjectFoldersBeforeBiggerPlainFolders_AndCandidatesIgnoreTheRoundLimit()
    {
        var options = new SmartCleanupOptions { DeepenMaxTargetsPerRound = 1 };
        var root = new NodeSnapshot { Path = "/r", Name = "r", IsDirectory = true, Depth = 0 };
        var games = Folder("games", root, 80L * 1024 * 1024 * 1024);
        var project = Folder("project", root, 300L * 1024 * 1024);
        var plain = Folder("plain", root, 1L * 1024 * 1024 * 1024);
        root.Children.AddRange(new[] { games, project, plain });
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [games] = new() { JunkScore = 0.1, DeletionRisk = 0.2, Confidence = 0.3, Tier = CleanupTier.Keep, Reasons = Array.Empty<Signal>() },
            [plain] = new() { JunkScore = 0.1, DeletionRisk = 0.2, Confidence = 0.3, Tier = CleanupTier.Keep, Reasons = Array.Empty<Signal>() },
            [project] = new()
            {
                JunkScore = 0.05, DeletionRisk = 0.5, Confidence = 0.3, Tier = CleanupTier.Keep,
                Reasons = new[] { new Signal(ReasonCode.ProjectRoot, EvidenceGroup.Structure, -1.8, 0.55, "project") },
            },
        };

        Assert.Equal(new[] { project }, DeepeningPlanner.SelectTargets(new[] { root }, map, options));
        Assert.Equal(new[] { project, games, plain }, DeepeningPlanner.Candidates(new[] { root }, map, options));
    }
}
