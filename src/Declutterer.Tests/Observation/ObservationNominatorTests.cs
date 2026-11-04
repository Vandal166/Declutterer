using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Cleanup;

namespace Declutterer.Tests.Observation;

public class ObservationNominatorTests : IDisposable
{
    private readonly CleanupScenario _s = new();
    private readonly ObservationOptions _o = new() { Enabled = true, ObserveMinBytes = 0 };

    public ObservationNominatorTests()
    {
        _s.Rules = JunkAssessmentEngine.DefaultRules(_o);
        _s.Machine.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
    }

    public void Dispose() => _s.Dispose();

    private static NodeSnapshot RootOf(AnalysisResult a)
    {
        var n = a.Assessments.Keys.First();
        while (n.Parent is not null)
            n = n.Parent;
        return n;
    }

    private ObservationDiscoveryResult Nominate(ScenarioResult r, IObservationLookup? existing = null, ObservationOptions? options = null,
        IEnumerable<string>? selectedPaths = null, ISet<string>? safeStaleKeepPaths = null) =>
        ObservationNominator.Nominate(new[] { RootOf(r.Analysis) }, r.Analysis, new SystemSnapshot(_s.Machine), options ?? _o, existing,
            selectedPaths, safeStaleKeepPaths);

    private string Rel(ObservationCandidate c) => Path.GetRelativePath(_s.Root, c.Path).Replace('\\', '/');

    private void OrphanApp(string name, int ageDays = 800, long size = 3000)
    {
        _s.File($"AppData/Roaming/{name}/settings.json", ageDays, size);
        _s.File($"AppData/Roaming/{name}/session.dat", ageDays, size);
    }

    [Fact]
    public void ASuspiciousButNotSafeFolder_IsNominated_WithItsClassAndScore()
    {
        OrphanApp("OldTool");
        _s.File("Documents/Taxes/2019-return.pdf", 1500, 100_000);

        var r = _s.Run();
        var candidates = Nominate(r);

        var c = Assert.Single(candidates.Candidates);
        Assert.Equal("AppData/Roaming/OldTool", Rel(c));
        Assert.Equal(ObservationClass.AppData, c.Class);
        Assert.Equal(CleanupTier.Review, c.Tier);
        Assert.True(c.JunkScore > 0.5);
        Assert.True(c.LastKnownActivityUtc.HasValue);
    }

    [Fact]
    public void SafeFolders_AreNotNominated_TheyNeedNoObservation()
    {
        _s.File("Projects/old-app/package.json", 800, 300);
        _s.File("Projects/old-app/node_modules/m/index.js", 800, 900);

        var r = _s.Run();

        Assert.Equal(CleanupTier.Safe, r.Tier("Projects/old-app/node_modules"));
        Assert.Empty(Nominate(r).Candidates);
    }

    [Fact]
    public void ALikelyFolder_IsNominatedAsAGenericFolder()
    {
        _s.File("Projects/active/package.json", 2, 300);
        _s.File("Projects/active/node_modules/m/index.js", 2, 900);

        var r = _s.Run();
        var c = Assert.Single(Nominate(r).Candidates);

        Assert.Equal("Projects/active/node_modules", Rel(c));
        Assert.Equal(ObservationClass.GenericFolder, c.Class);
        Assert.Equal(CleanupTier.Likely, c.Tier);
    }

    [Fact]
    public void AStaleKeepFolder_IsEligibleWhenItsFactsAndSafetyAreComplete()
    {
        _s.File("Misc/old/archive.bin", 1500, 5000);
        var run = _s.Run();
        const string relative = "Misc/old";
        Assert.Equal(CleanupTier.Keep, run.Tier(relative));
        var safePath = _s.Abs(relative);

        var result = Nominate(run, safeStaleKeepPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PathKey.Normalize(safePath) });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(CleanupTier.Keep, candidate.Tier);
        Assert.Equal(ObservationCandidateReason.Stale, candidate.Reason);
        Assert.Contains(ReasonCode.LongUntouched, candidate.ReasonCodes);
    }

    [Fact]
    public void AStaleKeepFolderWithIncompleteFacts_IsNotEligible()
    {
        _s.File("Misc/old/archive.bin", 1500, 5000);
        var run = _s.Run();
        var node = run.Analysis.Assessments.Keys.Single(n => n.Path.EndsWith(Path.Combine("Misc", "old"), StringComparison.OrdinalIgnoreCase));
        run.Analysis.Facts![node].Truncated = true;

        var result = Nominate(run, safeStaleKeepPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PathKey.Normalize(node.Path) });

        Assert.Empty(result.Candidates);
        Assert.True(result.Summary.ExcludedUnsafeOrIncomplete > 0);
    }

    [Fact]
    public void ACloudSyncedStaleKeepFolder_IsNotEligibleForWatching()
    {
        _s.File("Misc/old/archive.bin", 1500, 5000);
        var path = _s.Abs("Misc/old");
        _s.Machine.SyncedRoots.Add(path);
        var run = _s.Run();

        var result = Nominate(run, safeStaleKeepPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PathKey.Normalize(path) });

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void SelectedCandidateAncestorOrDescendant_IsNeverSuggestedForWatching()
    {
        _s.File("Projects/active/package.json", 2, 300);
        _s.File("Projects/active/node_modules/m/index.js", 2, 900);
        var run = _s.Run();
        var node = run.Analysis.Assessments.Keys.Single(n => n.Name == "node_modules");

        foreach (var selectedPath in new[]
                 {
                     node.Path,
                     Path.GetDirectoryName(node.Path)!,
                     Path.Combine(node.Path, "m"),
                 })
        {
            var result = Nominate(run, selectedPaths: new[] { selectedPath });

            Assert.Empty(result.Candidates);
            Assert.Equal(1, result.Summary.ExcludedSelectedOverlap);
        }
    }

    [Fact]
    public void FoldersWithUserDocumentsOrKeys_AreNeverNominated()
    {
        _s.File("Backups/old/id_rsa", 1500, 1700);
        _s.File("Backups/old/dump.bak", 1500, 90_000);
        _s.File("Documents/Taxes/notes.docx", 1500, 40_000);

        Assert.Empty(Nominate(_s.Run()).Candidates);
    }

    [Fact]
    public void FoldersBelowTheSizeHint_RemainEligible()
    {
        OrphanApp("OldTool");
        var r = _s.Run();

        Assert.Single(Nominate(r).Candidates);
        Assert.Single(Nominate(r, options: new ObservationOptions { ObserveMinBytes = 1L << 30 }).Candidates);
    }

    [Fact]
    public void Nominations_AreTopLevelOnly_AndLargestFirst()
    {
        OrphanApp("SmallTool", size: 1000);
        OrphanApp("BigTool", size: 90_000);
        _s.File("AppData/Roaming/BigTool/nested/cache.dat", 800, 5000); // a nested folder that is also suspicious

        var r = _s.Run();
        var candidates = Nominate(r).Candidates;

        Assert.Equal(new[] { "AppData/Roaming/BigTool", "AppData/Roaming/SmallTool" }, candidates.Select(Rel).ToArray());
        Assert.DoesNotContain(candidates, c => Rel(c).Contains("nested"));
    }

    [Fact]
    public void ThingsAlreadyWatched_PinnedOrSnoozed_AreSkipped_InsideAroundOrEqual()
    {
        OrphanApp("Watched");
        OrphanApp("PinnedTool");
        OrphanApp("Fresh");
        var now = DateTime.UtcNow;
        var lookup = new ObservationLookup(new[]
        {
            ObservationTestKit.Entry(_s.Abs("AppData/Roaming/Watched"), now),
            ObservationTestKit.Entry(_s.Abs("AppData/Roaming/PinnedTool"), now) with { State = WatchState.Resolved, Pinned = true, Resolution = WatchResolution.Pinned },
        });

        var r = _s.Run();
        var candidates = Nominate(r, lookup).Candidates;

        Assert.Equal(new[] { "AppData/Roaming/Fresh" }, candidates.Select(Rel).ToArray());
    }

    [Fact]
    public void AFolderThatContainsAWatchedFolder_IsSkippedToo()
    {
        OrphanApp("Suite");
        _s.File("AppData/Roaming/Suite/inner/data.dat", 800, 3000);
        var lookup = new ObservationLookup(new[] { ObservationTestKit.Entry(_s.Abs("AppData/Roaming/Suite/inner"), DateTime.UtcNow) });

        var candidates = Nominate(_s.Run(), lookup).Candidates;

        Assert.DoesNotContain(candidates, c => Rel(c) == "AppData/Roaming/Suite");
        Assert.DoesNotContain(candidates, c => Rel(c) == "AppData/Roaming/Suite/inner");
    }

    [Fact]
    public void TheWatchLimit_CapsHowManyAreOffered()
    {
        for (int i = 0; i < 5; i++)
            OrphanApp($"Tool{i}", size: 1000 + i * 100);
        var now = DateTime.UtcNow;
        var lookup = new ObservationLookup(new[]
        {
            ObservationTestKit.Entry("/elsewhere/a", now), ObservationTestKit.Entry("/elsewhere/b", now),
        });
        var options = new ObservationOptions { ObserveMinBytes = 0, MaxWatchItems = 4 }; // two used, two left

        var result = Nominate(_s.Run(), lookup, options);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(3, result.Summary.ExcludedByCapacity);
        Assert.Equal(new[] { "AppData/Roaming/Tool4", "AppData/Roaming/Tool3" }, result.Candidates.Select(Rel).ToArray()); // the largest ones
    }

    [Fact]
    public void TopLevel_KeepsTheOutermost()
    {
        ObservationCandidate C(string p) => new(p, "x", 1, ObservationClass.GenericFolder, CleanupTier.Review, 0.6, 0.3, null,
            ObservationCandidateReason.Suspicious, Array.Empty<ReasonCode>());

        var kept = ObservationNominator.TopLevel(new[] { C("/a/b/c"), C("/a/b"), C("/a/bb"), C("/z"), C("/a/b/c/d") });

        Assert.Equal(new[] { "/a/b", "/a/bb", "/z" }, kept.Select(c => c.Path).OrderBy(p => p).ToArray());
    }
}
