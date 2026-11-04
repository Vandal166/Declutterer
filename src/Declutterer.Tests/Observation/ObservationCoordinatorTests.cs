using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Observation;

public class ObservationCoordinatorTests : IDisposable
{
    private readonly CheckInRig _r = new();
    private ObservationCoordinator _c = null!;

    public ObservationCoordinatorTests()
    {
        _r.Options.Enabled = false; // the coordinator decides; it starts off, as in a fresh install
        _r.Build();
        Rebuild();
    }

    public void Dispose() => _r.Dispose();

    private void Rebuild() => _c = new ObservationCoordinator(_r.Ledger, _r.Service, new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow), _r.Walker,
        _r.Identity, _r.Options, _r.Reassessor, _r.Time);

    private ObservationCandidate Candidate(string rel, ObservationClass cls = ObservationClass.GenericFolder, DateTime? activity = null) =>
        new(_r.Item(rel), rel, 64L * 1024 * 1024, cls, CleanupTier.Likely, 0.8, 0.2, activity,
            ObservationCandidateReason.Suspicious, new[] { ReasonCode.KnownCacheLocation });

    private async Task<EnrollmentResult> EnrollAsync(params ObservationCandidate[] candidates)
    {
        await _c.SetEnabledAsync(true);
        return await _c.EnrollAsync(candidates);
    }

    // ── settings and consent ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSwitch_IsPersisted_AndReadBackAtStartup_AndConsentIsRecordedOnTheFirstEnable()
    {
        Assert.False(_c.IsEnabled);
        Assert.False(await _c.HasConsentAsync());

        await _c.SetEnabledAsync(true);
        Assert.True(_c.IsEnabled);
        Assert.True(await _c.HasConsentAsync());
        var consent = await _r.Ledger.GetSettingAsync(ObservationSettingKeys.ConsentUtc);

        _r.Time.AdvanceDays(3);
        await _c.SetEnabledAsync(false);
        await _c.SetEnabledAsync(true);
        Assert.Equal(consent, await _r.Ledger.GetSettingAsync(ObservationSettingKeys.ConsentUtc)); // asked once, not every time

        _r.Options.Enabled = false; // a new process: the options start at their default
        await _c.InitializeAsync();
        Assert.True(_c.IsEnabled);

        await _c.SetEnabledAsync(false);
        _r.Options.Enabled = true;
        await _c.InitializeAsync();
        Assert.False(_c.IsEnabled);
    }

    [Fact]
    public async Task ABrokenLedger_CanNotBeSwitchedOn()
    {
        var blocked = Path.Combine(_r.Dir.Path, "blocked");
        Directory.CreateDirectory(blocked);
        var broken = new SqliteObservationLedger(blocked, _r.Time);
        await broken.GetSettingAsync("x");
        var service = new ObservationCheckInService(broken, new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow), _r.Sources, _r.Walker, _r.Options);
        var coordinator = new ObservationCoordinator(broken, service, new FakeSnapshotProvider(_r.Machine, () => _r.Time.UtcNow), _r.Walker, _r.Identity, _r.Options);

        await coordinator.SetEnabledAsync(true);

        Assert.False(coordinator.IsEnabled);
        Assert.Equal(CheckInOutcome.Disabled, (await coordinator.RunCheckInAsync("x")).Outcome);
    }

    // ── enrolling ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrollingBeforeConsent_DoesNothing()
    {
        _r.CreateItem();

        var result = await _c.EnrollAsync(new[] { Candidate("item") });

        Assert.Equal(0, result.Enrolled);
        Assert.Contains("switched off", result.Skipped[0].Reason);
        Assert.Empty(await _c.GetWatchlistAsync());
    }

    [Fact]
    public async Task Enrolling_StoresABaseline_TheClassWindow_AndTheFileIdentity()
    {
        var path = _r.CreateItem("app");
        _r.Identity.Ids[path] = "vol:77";

        var result = await EnrollAsync(Candidate("app", ObservationClass.AppData));

        Assert.Equal(1, result.Enrolled);
        var e = Assert.Single(await _c.GetWatchlistAsync());
        Assert.Equal(WatchState.Watching, e.State);
        Assert.Equal(ObservationClass.AppData, e.Class);
        Assert.Equal(_r.Options.AppDataDays, e.RequiredDays);
        Assert.Equal("vol:77", e.FileId);
        Assert.Equal(_r.Time.UtcNow, e.QuietSinceUtc);
        Assert.NotNull(e.BaselineFingerprint);
        Assert.True(e.BaselineFingerprint!.IsComplete);
        Assert.Equal(e.BaselineFingerprint.RootHash, e.LastFingerprint!.RootHash);
        Assert.Equal(CleanupTier.Likely, e.NominatedTier);
        Assert.Contains(await _r.Ledger.GetOwnActivityAsync(_r.Time.UtcNow.AddDays(-1)), i => i.Kind == "enroll");
    }

    [Theory]
    [InlineData(ObservationClass.AppData, 28)]
    [InlineData(ObservationClass.GenericFolder, 45)]
    [InlineData(ObservationClass.ProjectFolder, 60)]
    public async Task TheRequiredCoveredDays_FollowTheClass(ObservationClass cls, int days)
    {
        _r.CreateItem();

        await EnrollAsync(Candidate("item", cls));

        Assert.Equal(days, (await _c.GetWatchlistAsync()).Single().RequiredDays);
    }

    [Fact]
    public async Task RecentUseBeforeEnrolling_StartsTheItemAsAlive_LikeObservedUse()
    {
        var path = _r.CreateItem();
        _r.Machine.RecentItems.Add(new RecentItem(Path.Combine(path, "sub", "f0.dat"), _r.Time.UtcNow.AddDays(-5)));

        var result = await EnrollAsync(Candidate("item"));

        Assert.Equal(1, result.AliveOnEnrollment);
        var e = (await _c.GetWatchlistAsync()).Single();
        Assert.Equal(WatchState.Alive, e.State);
        Assert.Equal(_r.Time.UtcNow.AddDays(-5).AddDays(_r.Options.AliveCooldownDays), e.AliveUntilUtc);
        Assert.Equal(UseStrength.Strong, e.LastActiveStrength);
    }

    [Fact]
    public async Task RecentContentActivity_FromTheAssessment_CountsAsUseToo()
    {
        _r.CreateItem();

        await EnrollAsync(Candidate("item", activity: _r.Time.UtcNow.AddDays(-2)));

        Assert.Equal(WatchState.Alive, (await _c.GetWatchlistAsync()).Single().State);
    }

    [Fact]
    public async Task UseThatIsOlderThanTheWindow_DoesNotMakeTheItemAlive()
    {
        var path = _r.CreateItem();
        _r.Machine.RecentItems.Add(new RecentItem(path, _r.Time.UtcNow.AddDays(-40)));

        await EnrollAsync(Candidate("item", activity: _r.Time.UtcNow.AddDays(-90)));

        Assert.Equal(WatchState.Watching, (await _c.GetWatchlistAsync()).Single().State);
    }

    [Fact]
    public async Task AppDataItems_UseTheShorterWindow_WhenTheCooldownIsLonger()
    {
        _r.CreateItem();

        await EnrollAsync(Candidate("item", ObservationClass.AppData, activity: _r.Time.UtcNow.AddDays(-29)));

        // 29 days ago is inside the 30-day cooldown but outside the 28-day app data window
        Assert.Equal(WatchState.Watching, (await _c.GetWatchlistAsync()).Single().State);
    }

    [Fact]
    public async Task Enrolling_SkipsWhatOverlapsWhatIsAlreadyWatched_Missing_Or_TooBigToFingerprint()
    {
        _r.CreateItem("a");
        _r.CreateItem("a/inner-extra");
        _r.CreateItem("b", files: 6);
        await EnrollAsync(Candidate("a"));

        var overlap = await _c.EnrollAsync(new[]
        {
            Candidate("a"),                 // equal
            Candidate("a/inner-extra"),     // inside
            Candidate("does-not-exist"),
        });
        Assert.Equal(0, overlap.Enrolled);
        Assert.Equal(3, overlap.Skipped.Count);
        Assert.Contains("already watched", overlap.Skipped[0].Reason);
        Assert.Contains("no longer exists", overlap.Skipped[2].Reason);

        _r.Options.FingerprintMaxEntries = 3;
        var big = await _c.EnrollAsync(new[] { Candidate("b") });
        Assert.Equal(0, big.Enrolled);
        Assert.Contains("fingerprinted completely", big.Skipped.Single().Reason);
        Assert.Single(await _c.GetWatchlistAsync()); // a failed enrollment leaves nothing behind
    }

    [Fact]
    public async Task EnrollingBatches_StopsAtTheWatchLimit_AndNeverNestsWithinTheBatch()
    {
        _r.Options.MaxWatchItems = 2;
        for (int i = 0; i < 4; i++)
            _r.CreateItem($"item{i}");
        _r.CreateItem("item0/child");

        var result = await EnrollAsync(Candidate("item0"), Candidate("item0/child"), Candidate("item1"), Candidate("item2"), Candidate("item3"));

        Assert.Equal(2, result.Enrolled);
        Assert.Equal(new[] { "item0", "item1" }, (await _c.GetWatchlistAsync()).Select(e => Path.GetFileName(e.DisplayPath)).ToArray());
        Assert.Contains(result.Skipped, s => s.Path.EndsWith("child") && s.Reason.Contains("already watched"));
        Assert.Contains(result.Skipped, s => s.Reason.Contains("limit"));
    }

    // ── watchlist actions ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopWatching_FinishesTheEntryAsDeclined_AndItMayBeEnrolledAgain()
    {
        _r.CreateItem();
        await EnrollAsync(Candidate("item"));
        var id = (await _c.GetWatchlistAsync()).Single().Id;

        Assert.True(await _c.StopWatchingAsync(id));

        Assert.Empty(await _c.GetWatchlistAsync());
        Assert.Equal(WatchResolution.Declined, (await _r.Ledger.GetAsync(id))!.Resolution);
        Assert.Equal(1, (await _c.EnrollAsync(new[] { Candidate("item") })).Enrolled);
    }

    [Fact]
    public async Task Pin_KeepsItForever_AndUnpinForgetsIt()
    {
        _r.CreateItem();
        await EnrollAsync(Candidate("item"));
        var id = (await _c.GetWatchlistAsync()).Single().Id;

        Assert.True(await _c.PinAsync(id, true));

        var row = (await _c.GetWatchlistAsync()).Single(); // pinned rows stay on the list: they are the "never again" memory
        Assert.True(row.Pinned);
        Assert.Equal(0, (await _c.EnrollAsync(new[] { Candidate("item") })).Enrolled);

        Assert.True(await _c.PinAsync(id, false));
        Assert.Equal(1, (await _c.EnrollAsync(new[] { Candidate("item") })).Enrolled);
    }

    [Fact]
    public async Task Snooze_SetsTheSnoozeDays()
    {
        _r.CreateItem();
        await EnrollAsync(Candidate("item"));
        var id = (await _c.GetWatchlistAsync()).Single().Id;

        Assert.True(await _c.SnoozeAsync(id));

        Assert.Equal(_r.Time.UtcNow.AddDays(_r.Options.SnoozeDays), (await _r.Ledger.GetAsync(id))!.SnoozeUntilUtc);
    }

    [Fact]
    public async Task Reenroll_GivesAGivenUpItemAFreshBaseline_AndOnlyThen()
    {
        var path = _r.CreateItem();
        await EnrollAsync(Candidate("item"));
        var e = (await _c.GetWatchlistAsync()).Single();
        Assert.False(await _c.ReenrollAsync(e.Id)); // still watching: nothing to do

        await _r.Ledger.UpdateAsync(e with { State = WatchState.Unobservable, InconclusiveStreak = 6, CoveredDays = 2 });
        _r.Dir.CreateFile("item/sub/added-later.dat", 99);
        _r.Time.AdvanceDays(10);

        Assert.True(await _c.ReenrollAsync(e.Id));

        var again = (await _r.Ledger.GetAsync(e.Id))!;
        Assert.Equal(WatchState.Watching, again.State);
        Assert.Equal(0, again.CoveredDays);
        Assert.Equal(0, again.InconclusiveStreak);
        Assert.Equal(_r.Time.UtcNow, again.QuietSinceUtc);
        Assert.NotEqual(e.BaselineFingerprint!.RootHash, again.BaselineFingerprint!.RootHash); // the new file is part of the new normal
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task Reassess_AndClearAll_Delegate()
    {
        _r.CreateItem();
        await EnrollAsync(Candidate("item"));
        var id = (await _c.GetWatchlistAsync()).Single().Id;

        var result = await _c.ReassessAsync(id);
        Assert.NotNull(result);
        Assert.Equal(new[] { id }, _r.Reassessor.Calls);

        await _c.ClearAllDataAsync();
        Assert.Empty(await _c.GetWatchlistAsync());
        Assert.True(await _c.HasConsentAsync()); // clearing data is not withdrawing consent
    }

    // ── startup ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartupCatchUp_RunsTheMissedCheckIn_OnlyWhenTheFeatureWasLeftOn()
    {
        _r.CreateItem();
        await EnrollAsync(Candidate("item"));
        _r.Time.AdvanceDays(1);
        _r.Options.Enabled = false; // a new process

        await _c.StartupCatchUpAsync();
        var run = await _c.GetLastRunAsync();
        Assert.True(_c.IsEnabled);
        Assert.Equal("startup", run!.Trigger);
        Assert.Equal(1, run.ItemsChecked);

        await _c.SetEnabledAsync(false);
        _r.Time.AdvanceDays(1);
        await _c.StartupCatchUpAsync();
        Assert.Equal(1, (await _r.Ledger.GetCheckInsAsync((await _c.GetWatchlistAsync()).Single().Id, 10)).Count);
    }

    [Fact]
    public async Task TheWholeLifecycle_FromCandidateToQuietEligible()
    {
        _r.CreateItem();
        _r.Options.GenericFolderDays = 4; // keep the test short: the window is fixed when the item is enrolled
        await EnrollAsync(Candidate("item"));
        var e = (await _c.GetWatchlistAsync()).Single();
        Assert.Equal(4, e.RequiredDays);

        for (int i = 0; i < 4; i++)
        {
            _r.Time.AdvanceDays(1);
            await _c.RunCheckInAsync("test");
        }

        var done = (await _c.GetWatchlistAsync()).Single();
        Assert.Equal(WatchState.QuietEligible, done.State);
        Assert.Equal(new[] { e.Id }, _r.Reassessor.Calls);
    }
}
