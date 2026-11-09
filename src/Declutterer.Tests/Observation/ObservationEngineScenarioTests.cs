using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Cleanup.Rules;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Cleanup;

namespace Declutterer.Tests.Observation;

/// <summary>The real engine on real folders, with a watch ledger's view loaded into the session.</summary>
public class ObservationEngineScenarioTests : IDisposable
{
    private readonly CleanupScenario _s = new();
    private readonly DateTime _now = DateTime.UtcNow;

    public ObservationEngineScenarioTests()
    {
        // scenario folders are tiny: observation's minimum size is a separate rule, tested on its own
        _s.Rules = JunkAssessmentEngine.DefaultRules(new ObservationOptions { ObserveMinBytes = 0 });
        _s.Machine.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
    }

    public void Dispose() => _s.Dispose();

    private WatchEntry Entry(string rel, Func<WatchEntry, WatchEntry>? shape = null)
    {
        var e = ObservationTestKit.Entry(_s.Abs(rel), _now.AddDays(-60), ObservationClass.AppData, 28);
        return shape is null ? e : shape(e);
    }

    private static WatchEntry QuietEligible(WatchEntry e) => e with
    {
        State = WatchState.QuietEligible, CoveredDays = 28, RequiredDays = 28, ValidUntilUtc = DateTime.UtcNow.AddDays(20),
    };

    private ScenarioResult Run(params WatchEntry[] watched)
    {
        _s.Session = new AnalysisSession { Observations = new ObservationLookup(watched) };
        return _s.Run();
    }

    private void OrphanedRoamingApp()
    {
        _s.File("AppData/Roaming/OldTool/settings.json", 800, 3000);
        _s.File("AppData/Roaming/OldTool/session.dat", 800, 9000);
    }

    private const string Orphan = "AppData/Roaming/OldTool";

    [Fact]
    public void WithoutAnyObservation_NothingChanges()
    {
        OrphanedRoamingApp();

        var r = Run();

        Assert.Equal(CleanupTier.Review, r.Tier(Orphan));
        Assert.DoesNotContain(r[Orphan].Reasons, x => x.Group == EvidenceGroup.Observation);
    }

    [Fact]
    public void OrphanedRoamingAppData_PlusCompleteQuiet_BecomesLikely_NotSafe_D1()
    {
        OrphanedRoamingApp();

        var r = Run(Entry(Orphan, QuietEligible));

        Assert.True(r.Has(Orphan, ReasonCode.OrphanedAppData), r.Explain(Orphan));
        Assert.True(r.Has(Orphan, ReasonCode.ObservedInactive), r.Explain(Orphan));
        Assert.Equal(CleanupTier.Likely, r.Tier(Orphan));
        Assert.True(r[Orphan].DeletionRisk > 0.15, r.Explain(Orphan)); // relieved, but never below what Safe tolerates
        Assert.Contains(Orphan, r.Selected(SmartSelectMode.Balanced));
        Assert.DoesNotContain(Orphan, r.Selected(SmartSelectMode.Conservative));
    }

    [Fact]
    public void TheSameItemWithAHardKeep_StaysKeep()
    {
        var exe = _s.File(Orphan + "/tool.exe", 800, 8000);
        _s.File(Orphan + "/settings.json", 800, 3000);
        _s.Machine.RunningExecutables.Add(exe);

        var r = Run(Entry(Orphan, QuietEligible));

        Assert.True(r[Orphan].HasHardKeep, r.Explain(Orphan));
        Assert.Equal(CleanupTier.Keep, r.Tier(Orphan));
        Assert.DoesNotContain(Orphan, r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void ACloudSyncedItem_KeepsARiskOfAtLeastPointThree()
    {
        _s.Machine.SyncedRoots.Add(_s.Abs("OneDrive"));
        _s.File("OneDrive/Projects/app/package.json", 800, 300);
        _s.File("OneDrive/Projects/app/node_modules/x/index.js", 800, 900);
        const string nm = "OneDrive/Projects/app/node_modules";

        var r = Run(Entry(nm, QuietEligible));

        Assert.True(r.Has(nm, ReasonCode.SyncedFolder));
        Assert.True(r[nm].DeletionRisk >= 0.3 - 1e-9, r.Explain(nm));
        Assert.NotEqual(CleanupTier.Safe, r.Tier(nm));
        Assert.DoesNotContain(nm, r.Selected(SmartSelectMode.Conservative));
    }

    [Fact]
    public void ObservedUse_DemotesAnItemThatWasReview()
    {
        OrphanedRoamingApp();
        var alive = Entry(Orphan, e => e with { State = WatchState.Alive, AliveUntilUtc = DateTime.UtcNow.AddDays(20), LastActiveUtc = DateTime.UtcNow.AddDays(-3),
            LastActiveStrength = UseStrength.Strong, LastActiveDetail = "opened 3 days ago (shell recent items)" });

        var r = Run(alive);

        Assert.True(r.Has(Orphan, ReasonCode.ObservedActive), r.Explain(Orphan));
        Assert.True(r.Tier(Orphan) < CleanupTier.Review, r.Explain(Orphan));
        Assert.Empty(r.Selected(SmartSelectMode.Aggressive));
        Assert.Contains("observed in use", r.Explain(Orphan));
    }

    [Fact]
    public void ARetiredItem_IsStillDemoted()
    {
        OrphanedRoamingApp();

        var r = Run(Entry(Orphan, e => e with { State = WatchState.Retired, AliveResets = 3 }));

        Assert.True(r.Tier(Orphan) < CleanupTier.Review, r.Explain(Orphan));
        Assert.Contains("kept coming back to life", r.Explain(Orphan));
    }

    [Fact]
    public void QuietThatHasExpired_IsIgnored()
    {
        OrphanedRoamingApp();

        var r = Run(Entry(Orphan, e => QuietEligible(e) with { ValidUntilUtc = DateTime.UtcNow.AddDays(-1) }));

        Assert.False(r.Has(Orphan, ReasonCode.ObservedInactive));
        Assert.Equal(CleanupTier.Review, r.Tier(Orphan));
    }

    [Fact]
    public void ARecentWeakSignal_HoldsBackQuiet_AndIsShown()
    {
        OrphanedRoamingApp();

        var r = Run(Entry(Orphan, e => QuietEligible(e) with
        {
            LastActiveUtc = DateTime.UtcNow.AddDays(-3), LastActiveStrength = UseStrength.Weak, LastActiveDetail = "a file was last accessed 3 days ago",
        }));

        Assert.False(r.Has(Orphan, ReasonCode.ObservedInactive));
        Assert.True(r.Has(Orphan, ReasonCode.ObservedActive));
        Assert.Contains(r[Orphan].Reasons, x => x.Code == ReasonCode.ObservedActive && x.JunkLlr < 0 && x.Risk <= 0.1 + 1e-9);
    }

    [Fact]
    public void AMovedItem_IsReportedAsMoved()
    {
        OrphanedRoamingApp();

        var r = Run(Entry(Orphan, e => e with { LastActiveUtc = DateTime.UtcNow.AddDays(-1), LastActiveStrength = UseStrength.Weak, LastActiveDetail = "moved or renamed to 'x'" }));

        Assert.True(r.Has(Orphan, ReasonCode.ObservedMoved));
    }

    [Fact]
    public void AWeakSignalThatHasAgedOut_NoLongerHoldsBackQuiet()
    {
        OrphanedRoamingApp();

        var r = Run(Entry(Orphan, e => QuietEligible(e) with { LastActiveUtc = DateTime.UtcNow.AddDays(-20), LastActiveStrength = UseStrength.Weak }));

        Assert.True(r.Has(Orphan, ReasonCode.ObservedInactive));
    }

    [Fact]
    public void AnUnobservableItem_GetsOnlyASmallCoveragePenalty()
    {
        OrphanedRoamingApp();
        var plain = Run();
        var plainRisk = plain[Orphan].DeletionRisk;

        var r = Run(Entry(Orphan, e => e with { State = WatchState.Unobservable }));

        Assert.True(r.Has(Orphan, ReasonCode.ObservationInconclusive));
        Assert.Equal(plain[Orphan].JunkScore, r[Orphan].JunkScore, 6);
        Assert.True(r[Orphan].DeletionRisk - plainRisk < 0.11);
    }

    [Fact]
    public void QuietEvidence_NeedsEnoughCoveredDays_ButNoLongerHasASizeGate()
    {
        OrphanedRoamingApp();

        var short_ = Run(Entry(Orphan, e => QuietEligible(e) with { CoveredDays = 10 })); // state says eligible, the numbers do not
        Assert.False(short_.Has(Orphan, ReasonCode.ObservedInactive));

        _s.Rules = JunkAssessmentEngine.DefaultRules(new ObservationOptions { ObserveMinBytes = 1L << 40 });
        var tiny = Run(Entry(Orphan, QuietEligible));
        Assert.True(tiny.Has(Orphan, ReasonCode.ObservedInactive));
    }

    [Fact]
    public void APinnedOrSnoozedItem_IsNeverSelected()
    {
        OrphanedRoamingApp();
        _s.File("AppData/Roaming/OtherTool/settings.json", 800, 3000);

        var pinned = Run(Entry(Orphan, e => e with { Pinned = true, State = WatchState.Resolved, Resolution = WatchResolution.Pinned }));
        Assert.True(pinned[Orphan].HasHardKeep);
        Assert.Equal(CleanupTier.Keep, pinned.Tier(Orphan));
        Assert.True(pinned.Has(Orphan, ReasonCode.PinnedByUser));

        var snoozed = Run(Entry("AppData/Roaming/OtherTool", e => QuietEligible(e) with { SnoozeUntilUtc = DateTime.UtcNow.AddDays(5) }));
        Assert.True(snoozed["AppData/Roaming/OtherTool"].HasHardKeep);
        Assert.DoesNotContain("AppData/Roaming/OtherTool", snoozed.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void AnEntryForAnotherPath_DoesNotInfluenceThisItem()
    {
        OrphanedRoamingApp();

        var r = Run(Entry("AppData/Roaming/SomethingElse", QuietEligible));

        Assert.False(r.Has(Orphan, ReasonCode.ObservedInactive));
    }

    [Fact]
    public void TheEngineKeepsReturningFactsForTheNominator()
    {
        OrphanedRoamingApp();

        var r = Run();

        Assert.NotNull(r.Analysis.Facts);
        Assert.Contains(r.Analysis.Facts!, kv => kv.Key.Path.EndsWith("OldTool") && kv.Value.IsComplete);
    }

    [Fact]
    public void AFolderAboveAPinnedOne_IsAHardKeep_AndNothingInsideThePinnedOneIsSelected()
    {
        _s.File("AppData/Roaming/Suite/tool/settings.json", 800, 3000);
        _s.File("AppData/Roaming/Suite/tool/cache/old.tmp", 800, 2000); // junk by itself
        _s.File("AppData/Roaming/Suite/other/settings.json", 800, 3000);
        const string cache = "AppData/Roaming/Suite/tool/cache";

        // control: without the pin the cache inside the app folder is offered
        Assert.Contains(cache, Run().Selected(SmartSelectMode.Balanced));

        var r = Run(Entry("AppData/Roaming/Suite/tool", e => e with { Pinned = true, State = WatchState.Resolved, Resolution = WatchResolution.Pinned }));

        Assert.True(r["AppData/Roaming/Suite/tool"].HasHardKeep, r.Explain("AppData/Roaming/Suite/tool"));
        Assert.True(r.Has("AppData/Roaming/Suite", ReasonCode.ContainsKeptFolder), r.Explain("AppData/Roaming/Suite"));
        Assert.True(r["AppData/Roaming/Suite"].HasHardKeep);
        foreach (var mode in new[] { SmartSelectMode.Conservative, SmartSelectMode.Balanced, SmartSelectMode.Aggressive })
        {
            var selected = r.Selected(mode);
            Assert.DoesNotContain("AppData/Roaming/Suite", selected);
            Assert.DoesNotContain(selected, p => p.StartsWith("AppData/Roaming/Suite/tool", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AFolderAboveASnoozedOne_IsAHardKeep_ButOnlyWhileTheSnoozeRuns()
    {
        _s.File("AppData/Roaming/Suite/tool/settings.json", 800, 3000);
        _s.File("AppData/Roaming/Suite/tool/session.dat", 800, 9000);

        var snoozed = Run(Entry("AppData/Roaming/Suite/tool", e => QuietEligible(e) with { SnoozeUntilUtc = DateTime.UtcNow.AddDays(5) }));
        var lapsed = Run(Entry("AppData/Roaming/Suite/tool", e => QuietEligible(e) with { SnoozeUntilUtc = DateTime.UtcNow.AddDays(-1) }));

        Assert.True(snoozed.Has("AppData/Roaming/Suite", ReasonCode.ContainsKeptFolder));
        Assert.False(lapsed.Has("AppData/Roaming/Suite", ReasonCode.ContainsKeptFolder));
    }

    [Fact]
    public void TheLookup_KnowsWhichFoldersContainAKeptOne()
    {
        var now = DateTime.UtcNow;
        var lookup = new ObservationLookup(new[]
        {
            ObservationTestKit.Entry("/data/apps/pinned", now) with { State = WatchState.Resolved, Pinned = true, Resolution = WatchResolution.Pinned },
            ObservationTestKit.Entry("/data/snoozed/tool", now) with { SnoozeUntilUtc = now.AddDays(3) },
            ObservationTestKit.Entry("/data/lapsed/tool", now) with { SnoozeUntilUtc = now.AddDays(-3) },
            ObservationTestKit.Entry("/data/plain/watched", now),
        }, now);

        Assert.True(lookup.ContainsKeptPath("/data/apps"));
        Assert.True(lookup.ContainsKeptPath("/DATA"));
        Assert.True(lookup.ContainsKeptPath("/data/snoozed"));
        Assert.False(lookup.ContainsKeptPath("/data/apps/pinned"));   // not strictly below itself (the rule for the item handles that)
        Assert.False(lookup.ContainsKeptPath("/data/lapsed"));        // the snooze is over
        Assert.False(lookup.ContainsKeptPath("/data/plain"));         // only a plain watch below it
        Assert.False(lookup.ContainsKeptPath("/data/appsuite"));      // a name prefix is not an ancestor
    }
}
