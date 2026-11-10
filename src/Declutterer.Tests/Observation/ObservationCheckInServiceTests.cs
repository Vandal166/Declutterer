using System.Security.Cryptography;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Observation;

public class ObservationCheckInServiceTests : IDisposable
{
    private readonly CheckInRig _r = new();

    public ObservationCheckInServiceTests()
    {
        _r.Build();
    }

    public void Dispose() => _r.Dispose();

    // ── the happy path ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUntouchedItem_BecomesQuietEligible_AfterTheRequiredCoveredDays_AndIsReassessedOnce()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 5);

        for (int day = 1; day <= 4; day++)
        {
            var run = await _r.RunAfterDaysAsync(1);
            var now = await _r.GetAsync(e.Id);
            Assert.Equal(CheckInOutcome.Ok, run.Outcome);
            Assert.Equal(WatchState.Watching, now.State);
            Assert.Equal(day, now.CoveredDays);
        }

        var last = await _r.RunAfterDaysAsync(1);
        var done = await _r.GetAsync(e.Id);

        Assert.Equal(WatchState.QuietEligible, done.State);
        Assert.Equal(5, done.CoveredDays);
        Assert.Equal(_r.Time.UtcNow.AddDays(_r.Options.QuietValidityDays), done.ValidUntilUtc);
        Assert.Equal(1, last.QuietEligible);
        Assert.Equal(1, last.Reassessed);
        Assert.Equal(new[] { e.Id }, _r.Reassessor.Calls);
        Assert.Equal(5, (await _r.Ledger.GetCheckInsAsync(e.Id, 20)).Count);
        Assert.All(await _r.Ledger.GetCheckInsAsync(e.Id, 20), c => Assert.Equal(CheckInVerdict.Quiet, c.Verdict));
    }

    [Fact]
    public async Task AMachineThatWasMostlyOff_CreditsOnlyTheDaysItWasOn()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 45);

        await _r.RunAfterDaysAsync(10);
        await _r.RunAfterDaysAsync(10);
        await _r.RunAfterDaysAsync(10); // 30 calendar days, three check-ins

        var now = await _r.GetAsync(e.Id);
        Assert.Equal(3, now.CoveredDays);
        Assert.Equal(WatchState.Watching, now.State);
        Assert.Equal(0, now.InconclusiveStreak); // sparse is not "inconclusive" while the patience lasts
    }

    [Fact]
    public async Task WhenTheMachineIsOnTooRarely_CheckInsTurnInconclusive_AndNeverCountAsQuiet()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 5); // patience: 3 x 5 = 15 calendar days

        await _r.RunAfterDaysAsync(8);
        await _r.RunAfterDaysAsync(12); // day 20: more than 15 calendar days, only 2 covered days of 5

        var now = await _r.GetAsync(e.Id);
        Assert.Equal(1, now.CoveredDays);
        Assert.Equal(1, now.InconclusiveStreak);
        var rows = await _r.Ledger.GetCheckInsAsync(e.Id, 10);
        Assert.Equal(CheckInVerdict.Inconclusive, rows[0].Verdict);
    }

    [Fact]
    public async Task EveryRunRecordsTheHeartbeat_EvenWithNothingToWatch()
    {
        var empty = await _r.RunAsync();

        Assert.Equal(CheckInOutcome.Ok, empty.Outcome);
        Assert.Equal(new[] { _r.Time.UtcNow.Date }, await _r.Ledger.GetHeartbeatDaysAsync(_r.Time.UtcNow.Date.AddDays(-1)));
        var log = await _r.Ledger.GetLastRunAsync();
        Assert.Equal(CheckInOutcome.Ok, log!.Outcome);
        Assert.Contains(await _r.Ledger.GetOwnActivityAsync(_r.Time.UtcNow.AddDays(-1)), i => i.Kind == "checkin");
    }

    // ── use ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AChangeInsideTheItem_MakesItAlive_ThenACooldownEndsWithAFreshBaseline_AndQuietCountingRestarts()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 5);
        await _r.RunAfterDaysAsync(1);
        await _r.RunAfterDaysAsync(1);

        _r.Dir.CreateFile("item/sub/new-file.dat", 777); // something used the folder
        _r.Settle(); // keep the test about the persistent new file, not filesystem timestamp propagation
        var run = await _r.RunAfterDaysAsync(1);
        var alive = await _r.GetAsync(e.Id);

        Assert.Equal(1, run.Alive);
        Assert.Equal(WatchState.Alive, alive.State);
        Assert.Equal(0, alive.CoveredDays);
        Assert.Equal(UseStrength.Strong, alive.LastActiveStrength);
        Assert.Contains("created", alive.LastActiveDetail);
        var aliveUntil = _r.Time.UtcNow.AddDays(_r.Options.AliveCooldownDays);
        Assert.Equal(aliveUntil, alive.AliveUntilUtc);

        // unchanged since: it stays alive for the whole cooldown and the cooldown is not extended by the old change
        for (int i = 0; i < _r.Options.AliveCooldownDays - 1; i++)
            await _r.RunAfterDaysAsync(1);
        var stillAlive = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Alive, stillAlive.State);
        Assert.Equal(aliveUntil, stillAlive.AliveUntilUtc);

        await _r.RunAfterDaysAsync(1); // the cooldown has passed
        var reset = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Watching, reset.State);
        Assert.Equal(1, reset.AliveResets);
        Assert.Equal(0, reset.CoveredDays);
        Assert.Equal(_r.Time.UtcNow, reset.QuietSinceUtc);
        Assert.NotNull(reset.BaselineFingerprint);

        await _r.RunAfterDaysAsync(1); // quiet against the NEW baseline: the file added earlier is no longer "a change"
        var counting = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Watching, counting.State);
        Assert.Equal(1, counting.CoveredDays);
    }

    [Fact]
    public async Task OpeningSomethingInsideTheItem_IsStrongUse_ButTheSameOpenDoesNotKeepExtendingTheCooldown()
    {
        var path = _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 5);
        await _r.RunAfterDaysAsync(1);
        var openedAt = _r.Time.UtcNow.AddHours(-2);
        _r.Machine.RecentItems.Add(new RecentItem(Path.Combine(path, "sub", "f0.dat"), openedAt));

        await _r.RunAfterDaysAsync(1);
        var alive = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Alive, alive.State);
        Assert.Contains("opened", alive.LastActiveDetail);
        Assert.Equal(openedAt, alive.LastActiveUtc);

        await _r.RunAfterDaysAsync(5);
        var later = await _r.GetAsync(e.Id);
        Assert.Equal(alive.AliveUntilUtc, later.AliveUntilUtc); // the same open is not new evidence
    }

    [Fact]
    public async Task AnOpenFromBeforeTheQuietClockStarted_IsIgnored()
    {
        var path = _r.CreateItem();
        _r.Machine.RecentItems.Add(new RecentItem(path, _r.Time.UtcNow.AddDays(-3)));
        var e = await _r.EnrollAsync(requiredDays: 5);

        await _r.RunAfterDaysAsync(1);

        Assert.Equal(WatchState.Watching, (await _r.GetAsync(e.Id)).State);
    }

    [Fact]
    public async Task AnItemThatIsReplaced_IsStrongUse()
    {
        var path = _r.CreateItem();
        _r.Identity.Ids[path] = "vol:1";
        var e = await _r.EnrollAsync(requiredDays: 5);
        Assert.Equal("vol:1", e.FileId);

        _r.Identity.Ids[path] = "vol:2"; // same name, different folder underneath
        await _r.RunAfterDaysAsync(1);

        var now = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Alive, now.State);
        Assert.Contains("recreated", now.LastActiveDetail);
        Assert.Equal("vol:2", now.FileId);
    }

    // ── weak signals ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShellDroppings_AreOnlyWeak_AreNotReportedAgain_AndHoldBackQuietForTheWeakCooldown()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 3);
        await _r.RunAfterDaysAsync(1);
        await _r.RunAfterDaysAsync(1);

        File.WriteAllText(_r.Item("item/sub/Thumbs.db"), "explorer was here");
        File.WriteAllText(_r.Item("item/desktop.ini"), "[.ShellClassInfo]");
        _r.Settle(); // keep the test about shell-dropping content, not filesystem timestamp propagation
        await _r.RunAfterDaysAsync(1);
        var weakDay = _r.Time.UtcNow;
        var weak = await _r.GetAsync(e.Id);

        Assert.Equal(WatchState.Watching, weak.State);          // three covered days, but a weak signal is fresh
        Assert.Equal(3, weak.CoveredDays);                      // and nothing was reset
        Assert.Equal(UseStrength.Weak, weak.LastActiveStrength);
        Assert.Equal(weakDay, weak.LastActiveUtc);
        Assert.Equal((await _r.Ledger.GetCheckInsAsync(e.Id, 1))[0].Verdict, CheckInVerdict.WeakUse);

        for (int i = 0; i < _r.Options.WeakCooldownDays - 1; i++)
        {
            await _r.RunAfterDaysAsync(1);
            var s = await _r.GetAsync(e.Id);
            Assert.Equal(WatchState.Watching, s.State);
            Assert.Equal(CheckInVerdict.Quiet, (await _r.Ledger.GetCheckInsAsync(e.Id, 1))[0].Verdict); // absorbed: not reported again
        }

        await _r.RunAfterDaysAsync(1);
        Assert.Equal(WatchState.QuietEligible, (await _r.GetAsync(e.Id)).State);
    }

    [Fact]
    public async Task ShellDroppingsNextToARealChange_AreStrongUse()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 3);

        File.WriteAllText(_r.Item("item/Thumbs.db"), "x");
        File.WriteAllText(_r.Item("item/real.txt"), "a real file");
        await _r.RunAfterDaysAsync(1);

        Assert.Equal(WatchState.Alive, (await _r.GetAsync(e.Id)).State);
    }

    private async Task<WatchEntry> EnrollWithAccessTimes(int files = 3)
    {
        _r.Machine.LastAccessTimeReliable = true;
        _r.CreateItem(files: files);
        return await _r.EnrollAsync(requiredDays: 5);
    }

    [Fact]
    public async Task ALastAccessTime_IsWeak_WhenTheFileSystemMaintainsThem()
    {
        var e = await EnrollWithAccessTimes();
        var enrolled = _r.Time.UtcNow;
        await _r.RunAfterDaysAsync(1);

        File.SetLastAccessTimeUtc(_r.Item("item/sub/f1.dat"), enrolled.AddHours(12)); // between enrollment and the next check-in
        await _r.RunAfterDaysAsync(1);

        var now = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Watching, now.State);
        Assert.Equal(UseStrength.Weak, now.LastActiveStrength);
        Assert.Equal(enrolled.AddHours(12), now.LastActiveUtc);
        Assert.Equal(2, now.CoveredDays); // weak never resets coverage
    }

    [Fact]
    public async Task ALastAccessTime_IsIgnored_WhenTimestampsAreNotMaintained()
    {
        var e = await EnrollWithAccessTimes();
        _r.Machine.LastAccessTimeReliable = false; // and the sample is far too small for the probe to say otherwise
        var enrolled = _r.Time.UtcNow;
        await _r.RunAfterDaysAsync(1);

        File.SetLastAccessTimeUtc(_r.Item("item/sub/f1.dat"), enrolled.AddHours(12));
        await _r.RunAfterDaysAsync(1);

        Assert.Equal(UseStrength.None, (await _r.GetAsync(e.Id)).LastActiveStrength);
    }

    [Fact]
    public async Task ALastAccessTime_InsideOurOwnActivityWindow_IsIgnored()
    {
        var e = await EnrollWithAccessTimes();
        var enrolled = _r.Time.UtcNow;
        await _r.RunAfterDaysAsync(1);
        var firstRun = _r.Time.UtcNow;

        File.SetLastAccessTimeUtc(_r.Item("item/sub/f1.dat"), firstRun.AddMinutes(2)); // right after our own first walk
        await _r.RunAfterDaysAsync(1);

        Assert.Equal(UseStrength.None, (await _r.GetAsync(e.Id)).LastActiveStrength);
        Assert.True(enrolled < firstRun);
    }

    [Fact]
    public async Task ATreeWideAccessBurst_IsNotAPerson()
    {
        var e = await EnrollWithAccessTimes(files: 30);
        var enrolled = _r.Time.UtcNow;
        await _r.RunAfterDaysAsync(1);

        foreach (var f in Directory.EnumerateFiles(_r.Item("item"), "*", SearchOption.AllDirectories))
            File.SetLastAccessTimeUtc(f, enrolled.AddHours(12)); // an antivirus scan touched everything
        await _r.RunAfterDaysAsync(1);

        Assert.Equal(UseStrength.None, (await _r.GetAsync(e.Id)).LastActiveStrength);
    }

    // ── unknown is never quiet ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ATruncatedWalk_IsInconclusive_NeverQuiet_AndTheItemEndsUpUnobservable()
    {
        _r.CreateItem(files: 5);
        var e = await _r.EnrollAsync(requiredDays: 3);
        _r.Options.FingerprintMaxEntries = 2; // far fewer than the 7 entries in the item
        _r.Options.MaxInconclusiveAttempts = 3;

        for (int day = 1; day <= 2; day++)
        {
            await _r.RunAfterDaysAsync(1);
            var s = await _r.GetAsync(e.Id);
            Assert.Equal(WatchState.Watching, s.State);
            Assert.Equal(0, s.CoveredDays);
            Assert.Equal(day, s.InconclusiveStreak);
        }

        await _r.RunAfterDaysAsync(1);
        Assert.Equal(WatchState.Unobservable, (await _r.GetAsync(e.Id)).State);

        await _r.RunAfterDaysAsync(60); // an unobservable item is not checked in any more, however long it sits there
        var after = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Unobservable, after.State);
        Assert.Equal(0, after.CoveredDays);
        Assert.All(await _r.Ledger.GetCheckInsAsync(e.Id, 20), c => Assert.Equal(CheckInVerdict.Inconclusive, c.Verdict));
    }

    [Fact]
    public async Task ASourceThatCrashes_MakesTheCheckInInconclusive_NotQuiet_AndTheRunStillFinishes()
    {
        _r.Sources.Add(new ThrowingSource());
        _r.Build();
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 3);

        var run = await _r.RunAfterDaysAsync(1);

        Assert.Equal(CheckInOutcome.Ok, run.Outcome);
        var now = await _r.GetAsync(e.Id);
        Assert.Equal(1, now.InconclusiveStreak);
        Assert.Equal(0, now.CoveredDays);
    }

    [Fact]
    public async Task ADeletedItem_IsResolvedAsMissing_AndSoIsAMovedOneWithoutFileIdentity()
    {
        _r.CreateItem("gone");
        _r.CreateItem("moved");
        var gone = await _r.EnrollAsync("gone");
        var moved = await _r.EnrollAsync("moved");
        Directory.Delete(_r.Item("gone"), recursive: true);
        Directory.Move(_r.Item("moved"), _r.Item("moved-elsewhere"));
        _r.Identity.IsSupported = false; // path-only platform

        var run = await _r.RunAfterDaysAsync(1);

        Assert.Equal(2, run.Missing);
        Assert.Equal(WatchResolution.Missing, (await _r.GetAsync(gone.Id)).Resolution);
        Assert.Equal(WatchState.Resolved, (await _r.GetAsync(moved.Id)).State); // a move reads as Missing, never as quiet
    }

    [Fact]
    public async Task ARenamedItem_IsRecognisedByItsFileIdentity_AndReanchored()
    {
        var path = _r.CreateItem("tool");
        _r.Identity.Ids[path] = "vol:42";
        var e = await _r.EnrollAsync("tool", requiredDays: 5);
        await _r.RunAfterDaysAsync(1);

        var renamed = _r.Item("tool-renamed");
        Directory.Move(path, renamed);
        _r.Identity.Ids.Remove(path);
        _r.Identity.Ids[renamed] = "vol:42";
        await _r.RunAfterDaysAsync(1);

        var now = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Watching, now.State);
        Assert.Equal(renamed, now.DisplayPath);
        Assert.Equal(ObservationKey.From(renamed), now.Key);
        Assert.Equal(UseStrength.Weak, now.LastActiveStrength);
        Assert.Contains("moved or renamed", now.LastActiveDetail);
        Assert.Equal(2, now.CoveredDays);

        await _r.RunAfterDaysAsync(1); // and from now on it is simply quiet at its new place
        Assert.Equal(CheckInVerdict.Quiet, (await _r.Ledger.GetCheckInsAsync(e.Id, 1))[0].Verdict);
    }

    // ── the run itself ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADisabledFeature_DoesNothing()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync();
        _r.Options.Enabled = false;

        var run = await _r.RunAfterDaysAsync(1);

        Assert.Equal(CheckInOutcome.Disabled, run.Outcome);
        Assert.Null((await _r.GetAsync(e.Id)).LastCheckInUtc);
        Assert.Empty(await _r.Ledger.GetHeartbeatDaysAsync(DateTime.UnixEpoch));
    }

    [Fact]
    public async Task ARunLockHeldByAnotherProcess_SkipsTheRun_UntilItIsReleased()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync();
        _r.Time.AdvanceDays(1);
        Assert.True(await _r.Ledger.TryAcquireRunLockAsync("headless:1", TimeSpan.FromMinutes(5), _r.Time.UtcNow));

        var blocked = await _r.RunAsync();
        Assert.Equal(CheckInOutcome.LockHeld, blocked.Outcome);
        Assert.Null((await _r.GetAsync(e.Id)).LastCheckInUtc);
        Assert.Equal(CheckInOutcome.LockHeld, (await _r.Ledger.GetLastRunAsync())!.Outcome);

        await _r.Ledger.ReleaseRunLockAsync("headless:1");
        var ok = await _r.RunAsync();
        Assert.Equal(CheckInOutcome.Ok, ok.Outcome);
        Assert.Equal(1, ok.ItemsChecked);

        // and the lock is released again after a normal run
        Assert.True(await _r.Ledger.TryAcquireRunLockAsync("someone", TimeSpan.FromMinutes(5), _r.Time.UtcNow));
    }

    [Fact]
    public async Task ACancelledRun_ReportsIt_AndReleasesTheLock()
    {
        _r.CreateItem();
        await _r.EnrollAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = await _r.Service.RunAsync("test", cts.Token).ContinueWith(t => t.IsCanceled ? null : t.Result);

        Assert.True(run is null || run.Outcome == CheckInOutcome.Cancelled);
        Assert.Equal(CheckInOutcome.Ok, (await _r.RunAsync()).Outcome); // not stuck behind a stale lock
    }

    [Fact]
    public async Task SnoozedItems_AreSkipped_UntilTheSnoozeEnds()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync();
        await _r.Ledger.SnoozeAsync(e.Id, _r.Time.UtcNow.AddDays(10));

        await _r.RunAfterDaysAsync(1);
        Assert.Null((await _r.GetAsync(e.Id)).LastCheckInUtc);

        await _r.RunAfterDaysAsync(10);
        Assert.NotNull((await _r.GetAsync(e.Id)).LastCheckInUtc);
    }

    [Fact]
    public async Task SnoozedHeartbeatsDoNotAdvanceCoverage_AndEarlierProgressIsKept()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 20);
        await _r.RunAfterDaysAsync(1);
        await _r.RunAfterDaysAsync(1);
        Assert.Equal(2, (await _r.GetAsync(e.Id)).CoveredDays);

        await _r.Ledger.SnoozeAsync(e.Id, _r.Time.UtcNow.AddDays(10));
        await _r.RunAfterDaysAsync(10);

        Assert.Equal(2, (await _r.GetAsync(e.Id)).CoveredDays);
        await _r.RunAfterDaysAsync(1);
        Assert.Equal(3, (await _r.GetAsync(e.Id)).CoveredDays);
    }

    [Fact]
    public async Task TheTimeBudget_DefersTheRest_AndTheNextRunStartsWithTheItemsCheckedLongestAgo()
    {
        var slow = new SlowSource(_r.Time, TimeSpan.FromSeconds(40));
        _r.Sources.Clear();
        _r.Sources.Add(slow);
        _r.Options.CheckInMaxDegreeOfParallelism = 1;
        _r.Build();
        foreach (var name in new[] { "a", "b", "c" })
        {
            _r.CreateItem(name);
            await _r.EnrollAsync(name);
        }

        var first = await _r.RunAfterDaysAsync(1); // 60 s budget: items start at 0 s and 40 s, the third would start at 80 s
        Assert.Equal(2, first.ItemsChecked);
        Assert.Equal(1, first.Deferred);
        var neverChecked = (await _r.Ledger.ListAsync(WatchListFilter.Active)).Single(e => e.LastCheckInUtc is null);

        slow.Seen.Clear();
        await _r.RunAfterDaysAsync(1);

        Assert.Equal(neverChecked.DisplayPath, slow.Seen[0]); // the deferred one goes first: nobody starves
    }

    [Fact]
    public async Task ARunNeverReadsOrChangesTheWatchedFiles_Invariant1()
    {
        var path = _r.CreateItem(files: 4);
        _r.Machine.LastAccessTimeReliable = true;
        var e = await _r.EnrollAsync(requiredDays: 3);
        string Snapshot() => string.Join("|", Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f =>
        {
            var i = new FileInfo(f);
            return $"{f}:{i.Length}:{i.LastWriteTimeUtc.Ticks}:{i.LastAccessTimeUtc.Ticks}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}";
        }));
        // pin the access times again after the test's own content read above
        void Repin() { foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetLastAccessTimeUtc(f, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)); }
        var before = Snapshot();
        Repin();
        var metadataBefore = string.Join("|", Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => $"{f}:{new FileInfo(f).LastAccessTimeUtc.Ticks}"));

        for (int i = 0; i < 4; i++)
            await _r.RunAfterDaysAsync(1);

        var metadataAfter = string.Join("|", Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => $"{f}:{new FileInfo(f).LastAccessTimeUtc.Ticks}"));
        Assert.Equal(metadataBefore, metadataAfter);   // not even an access time moved
        Repin();
        Assert.Equal(before, Snapshot());              // content, size and write times identical
        Assert.Equal(WatchState.QuietEligible, (await _r.GetAsync(e.Id)).State);
    }

    // ── heartbeat from a change journal ──────────────────────────────────────────────────────

    [Fact]
    public async Task AChangeJournal_CanCreditDaysTheMachineWasOn_WithoutACheckIn()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 5);
        var start = _r.Time.UtcNow.Date;
        _r.Volume.Report = new VolumeActivityReport(true, Enumerable.Range(1, 5).Select(d => start.AddDays(d)).ToList(), false, null);

        await _r.RunAfterDaysAsync(6); // a single check-in: the journal proves days 1-5 were used, today makes six

        var now = await _r.GetAsync(e.Id);
        Assert.Equal(6, now.CoveredDays);
        Assert.Equal(WatchState.QuietEligible, now.State);
        Assert.Equal(1, (await _r.Ledger.GetCheckInsAsync(e.Id, 10)).Count);
    }

    [Fact]
    public async Task AnUnavailableJournal_AddsNothing()
    {
        _r.CreateItem();
        var e = await _r.EnrollAsync(requiredDays: 5);
        _r.Volume.Report = new VolumeActivityReport(false, new[] { _r.Time.UtcNow.Date.AddDays(1) }, true, "no journal");

        await _r.RunAfterDaysAsync(3);

        Assert.Equal(1, (await _r.GetAsync(e.Id)).CoveredDays);
    }

    [Fact]
    public async Task TheReassessmentLoop_StandsAside_WhenAnotherProcessTookOverTheExpiredRunLock()
    {
        _r.CreateItem("a");
        _r.CreateItem("b");
        await _r.EnrollAsync("a", requiredDays: 1);
        await _r.EnrollAsync("b", requiredDays: 1);
        // the first reassessment is so slow that the run lock (5 minutes) expires and the scheduled run takes it over
        _r.Reassessor.DuringEachCall = async () =>
        {
            _r.Time.Advance(TimeSpan.FromMinutes(6));
            await _r.Ledger.TryAcquireRunLockAsync("headless:9", TimeSpan.FromMinutes(5), _r.Time.UtcNow);
        };

        var run = await _r.RunAfterDaysAsync(1);

        Assert.Equal(CheckInOutcome.Ok, run.Outcome);
        Assert.Equal(2, run.QuietEligible);
        Assert.Equal(1, run.Reassessed);              // the second one was left for the next run, not run in parallel with the other process
        Assert.Single(_r.Reassessor.Calls);
    }

    [Fact]
    public async Task TheReassessmentLoop_RenewsTheRunLock_SoALongRunKeepsOtherProcessesOut()
    {
        _r.CreateItem("a");
        _r.CreateItem("b");
        await _r.EnrollAsync("a", requiredDays: 1);
        await _r.EnrollAsync("b", requiredDays: 1);
        bool otherGotIn = false;
        _r.Reassessor.DuringEachCall = async () =>
        {
            _r.Time.Advance(TimeSpan.FromMinutes(4)); // each one is slow, but the lock is renewed before every one
            otherGotIn |= await _r.Ledger.TryAcquireRunLockAsync("headless:9", TimeSpan.FromMinutes(5), _r.Time.UtcNow);
        };

        var run = await _r.RunAfterDaysAsync(1);

        Assert.Equal(2, run.Reassessed);
        Assert.False(otherGotIn);
    }
}
