using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace Declutterer.Tests.Observation;

public class ObservationLedgerTests : IDisposable
{
    private readonly TempTestDirectory _t = new();
    private readonly ManualTimeProvider _time = new();
    private readonly string _db;

    public ObservationLedgerTests()
    {
        _db = Path.Combine(_t.Path, "ledger", "ledger.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _t.Dispose();
    }

    private SqliteObservationLedger NewLedger() => new(_db, _time);

    private long RawLong(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private WatchEntry E(string path, WatchState state = WatchState.Watching) => ObservationTestKit.Entry(path, _time.UtcNow, state: state, baseline: ObservationTestKit.Print());

    [Fact]
    public async Task FreshDatabase_IsCreatedAndMigratedToTheCurrentVersion()
    {
        var ledger = NewLedger();

        Assert.NotNull(await ledger.EnrollAsync(E("/data/a")));

        Assert.True(ledger.IsAvailable);
        Assert.Equal(ObservationSchema.CurrentVersion, RawLong("PRAGMA user_version;"));
        Assert.Equal(1, RawLong("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ux_watch_item_active_path'"));
    }

    [Fact]
    public async Task VersionOneSnoozes_AreBackfilledWhenTheLedgerMigrates()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        using (var c = new SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            c.Open();
            using (var create = c.CreateCommand())
            {
                create.CommandText = ObservationSchema.Migrations[0] + "\nPRAGMA user_version = 1;";
                create.ExecuteNonQuery();
            }

            using var insert = c.CreateCommand();
            insert.CommandText = "INSERT INTO watch_item (path_key, display_path, class, state, enrolled_utc, quiet_since_utc, required_days, last_checkin_utc, snooze_until_utc) " +
                                 "VALUES ('/data/legacy', '/data/legacy', $class, $state, $enrolled, $quietSince, 45, $lastCheckIn, $snoozeUntil);";
            insert.Parameters.AddWithValue("$class", (int)ObservationClass.GenericFolder);
            insert.Parameters.AddWithValue("$state", (int)WatchState.Watching);
            insert.Parameters.AddWithValue("$enrolled", _time.UtcNow.AddDays(-20).Ticks);
            insert.Parameters.AddWithValue("$quietSince", _time.UtcNow.AddDays(-20).Ticks);
            insert.Parameters.AddWithValue("$lastCheckIn", _time.UtcNow.AddDays(-3).Ticks);
            insert.Parameters.AddWithValue("$snoozeUntil", _time.UtcNow.AddDays(7).Ticks);
            insert.ExecuteNonQuery();
        }

        var migrated = (await NewLedger().GetAsync(1))!;

        Assert.Equal(2, RawLong("PRAGMA user_version;"));
        Assert.Equal(_time.UtcNow.AddDays(-3), migrated.SnoozedAtUtc);
    }

    [Fact]
    public async Task Enroll_RoundTripsEveryField()
    {
        var ledger = NewLedger();
        var fp = ObservationTestKit.Print("beef");
        var entry = E("/data/Round Trip") with
        {
            FileId = "1:2:3", Class = ObservationClass.AppData, RequiredDays = 28, CoveredDays = 4, LastCheckInUtc = _time.UtcNow.AddDays(1),
            LastActiveUtc = _time.UtcNow.AddDays(-2), LastActiveStrength = UseStrength.Weak, LastActiveDetail = "weak thing",
            AliveUntilUtc = _time.UtcNow.AddDays(9), ValidUntilUtc = _time.UtcNow.AddDays(20), AliveResets = 2, InconclusiveStreak = 3,
            Sources = ObservationSource.SnapshotDiff | ObservationSource.Heartbeat, LastTier = CleanupTier.Review, LastScore = 0.61,
            LastReasons = "because", LastAssessedUtc = _time.UtcNow, SnoozeUntilUtc = _time.UtcNow.AddDays(30),
            SnoozedAtUtc = _time.UtcNow.AddDays(-1), LastFingerprint = fp,
        };

        var saved = await ledger.EnrollAsync(entry);
        var read = await NewLedger().GetAsync(saved!.Id); // a second instance: nothing is cached

        Assert.NotNull(read);
        Assert.Equal(entry with { Id = saved.Id, BaselineFingerprint = read!.BaselineFingerprint, LastFingerprint = read.LastFingerprint }, read);
        Assert.Equal("beef", read.LastFingerprint!.RootHash);
        Assert.Equal("aa", read.BaselineFingerprint!.RootHash);
        Assert.Equal(DateTimeKind.Utc, read.EnrolledUtc.Kind);
    }

    [Fact]
    public async Task APathIsWatchedOnce_EvenWhenWrittenInAnotherCase()
    {
        var ledger = NewLedger();

        Assert.NotNull(await ledger.EnrollAsync(E("/Data/Cache")));
        Assert.Null(await ledger.EnrollAsync(E("/data/CACHE")));
        Assert.Single(await ledger.ListAsync(WatchListFilter.All));
    }

    [Fact]
    public async Task AFinishedEntryAllowsReEnrollment_ButAPinnedOneDoesNot()
    {
        var ledger = NewLedger();
        var first = (await ledger.EnrollAsync(E("/data/a")))!;
        Assert.True(await ledger.ResolveAsync(first.Id, WatchResolution.Declined, _time.UtcNow));

        var again = await ledger.EnrollAsync(E("/data/a"));
        Assert.NotNull(again);
        Assert.NotEqual(first.Id, again!.Id);

        Assert.True(await ledger.SetPinnedAsync(again.Id, true, _time.UtcNow));
        Assert.Null(await ledger.EnrollAsync(E("/data/a")));

        Assert.True(await ledger.SetPinnedAsync(again.Id, false, _time.UtcNow)); // unpin forgets it
        Assert.NotNull(await ledger.EnrollAsync(E("/data/a")));
    }

    [Fact]
    public async Task RecordCheckIn_UpdatesTheEntryAndAddsARow_AtomicallyAndOnlyWhileActive()
    {
        var ledger = NewLedger();
        var e = (await ledger.EnrollAsync(E("/data/a")))!;
        var next = e with { CoveredDays = 5, LastCheckInUtc = _time.UtcNow, State = WatchState.Alive, AliveUntilUtc = _time.UtcNow.AddDays(30) };

        Assert.True(await ledger.RecordCheckInAsync(next, new CheckInRecord(e.Id, _time.UtcNow, CheckInVerdict.StrongUse, ObservationSource.SnapshotDiff, 0, "{\"created\":1}")));

        var read = (await ledger.GetAsync(e.Id))!;
        Assert.Equal(WatchState.Alive, read.State);
        Assert.Equal(5, read.CoveredDays);
        var rows = await ledger.GetCheckInsAsync(e.Id, 10);
        Assert.Single(rows);
        Assert.Equal(CheckInVerdict.StrongUse, rows[0].Verdict);
        Assert.Equal("{\"created\":1}", rows[0].ChangeCounts);

        // finished meanwhile (the user stopped watching): a late check-in must not resurrect it, nor leave a row behind
        await ledger.ResolveAsync(e.Id, WatchResolution.Declined, _time.UtcNow);
        Assert.False(await ledger.RecordCheckInAsync(next with { State = WatchState.Watching }, new CheckInRecord(e.Id, _time.UtcNow, CheckInVerdict.Quiet, ObservationSource.None, 0, null)));
        Assert.Equal(WatchState.Resolved, (await ledger.GetAsync(e.Id))!.State);
        Assert.Single(await ledger.GetCheckInsAsync(e.Id, 10));
    }

    [Fact]
    public async Task AReanchorOntoAnotherWatchedPath_IsRejectedWithoutThrowing()
    {
        var ledger = NewLedger();
        var a = (await ledger.EnrollAsync(E("/data/a")))!;
        await ledger.EnrollAsync(E("/data/b"));

        var moved = a with { Key = ObservationKey.From("/data/b"), DisplayPath = "/data/b" };

        Assert.False(await ledger.UpdateAsync(moved));
        Assert.Equal("/data/a", (await ledger.GetAsync(a.Id))!.DisplayPath);
    }

    [Fact]
    public async Task Heartbeats_AreOnePerDay_AndKeepEverySource()
    {
        var ledger = NewLedger();
        var day = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);

        await ledger.RecordHeartbeatAsync(day.AddHours(13), ObservationSource.Heartbeat);
        await ledger.RecordHeartbeatAsync(day.AddHours(20), ObservationSource.UsnJournal);
        await ledger.RecordHeartbeatAsync(day.AddDays(3), ObservationSource.Heartbeat);

        var days = await ledger.GetHeartbeatDaysAsync(day);
        Assert.Equal(new[] { day, day.AddDays(3) }, days);
        Assert.Equal((long)(ObservationSource.Heartbeat | ObservationSource.UsnJournal), RawLong("SELECT heartbeat_source FROM covered_day WHERE day = '2026-02-10'"));
        Assert.Single(await ledger.GetHeartbeatDaysAsync(day.AddDays(1)));
    }

    [Fact]
    public async Task TwoLedgersOnOneFile_WriteConcurrentlyWithoutLosingOrFailing()
    {
        var a = NewLedger();
        var b = NewLedger();
        await a.GetSettingAsync("warm-up"); // create + migrate once

        var tasks = Enumerable.Range(0, 60).Select(i => Task.Run(async () =>
        {
            var ledger = i % 2 == 0 ? a : b;
            var saved = await ledger.EnrollAsync(E($"/data/item{i}"));
            await ledger.RecordHeartbeatAsync(_time.UtcNow.Date.AddDays(i % 5), ObservationSource.Heartbeat);
            await ledger.RecordOwnActivityAsync(_time.UtcNow, _time.UtcNow.AddMinutes(1), "test");
            return saved;
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Equal(60, (await a.ListAsync(WatchListFilter.All)).Count);
        Assert.Equal(5, (await b.GetHeartbeatDaysAsync(_time.UtcNow.Date)).Count);
        Assert.Equal(60, (await a.GetOwnActivityAsync(_time.UtcNow.AddDays(-1))).Count);
    }

    [Fact]
    public async Task ACorruptLedger_IsMovedAsideAndReplaced_NeverCrashing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        File.WriteAllText(_db, "this is definitely not a sqlite database, just text that is long enough to look like a header....................");

        var ledger = NewLedger();
        var saved = await ledger.EnrollAsync(E("/data/a"));

        Assert.NotNull(saved);
        Assert.True(ledger.IsAvailable);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_db)!, "ledger.db.corrupt-*"));
        Assert.Single(await ledger.ListAsync(WatchListFilter.All));
    }

    [Fact]
    public async Task ALedgerFromANewerVersion_IsNeverTouched_AndGivesNoEvidence()
    {
        var first = NewLedger();
        await first.EnrollAsync(E("/data/a"));
        using (var c = new SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 99;";
            cmd.ExecuteNonQuery();
        }

        var ledger = NewLedger();
        var read = await ledger.ListAsync(WatchListFilter.All);
        var enrolled = await ledger.EnrollAsync(E("/data/b"));
        var lookup = await ledger.LoadLookupAsync();

        Assert.Empty(read);
        Assert.Null(enrolled);
        Assert.False(ledger.IsAvailable);
        Assert.False(lookup.OverlapsExisting("/data/a")); // no evidence at all, not even "this is watched"
        Assert.Equal(99, RawLong("PRAGMA user_version;"));
        Assert.Equal(1, RawLong("SELECT COUNT(*) FROM watch_item")); // untouched
    }

    [Fact]
    public async Task AnUnusableLocation_FailsSoftOnEveryCall()
    {
        var blocked = Path.Combine(_t.Path, "blocked");
        Directory.CreateDirectory(blocked); // a directory where the database file should be
        var ledger = new SqliteObservationLedger(blocked, _time);

        Assert.Null(await ledger.EnrollAsync(E("/data/a")));
        Assert.Empty(await ledger.ListAsync(WatchListFilter.Lookup));
        Assert.False(await ledger.TryAcquireRunLockAsync("me", TimeSpan.FromMinutes(1), _time.UtcNow));
        Assert.Null(await ledger.GetSettingAsync("enabled"));
        await ledger.RecordHeartbeatAsync(_time.UtcNow, ObservationSource.Heartbeat);
    }

    [Fact]
    public async Task Purge_ExpiresFinishedEntriesAndOldRows_ButKeepsPinnedOnes()
    {
        var ledger = NewLedger();
        var options = new ObservationOptions();
        var old = (await ledger.EnrollAsync(E("/data/old")))!;
        var fresh = (await ledger.EnrollAsync(E("/data/fresh")))!;
        var pinned = (await ledger.EnrollAsync(E("/data/pinned")))!;
        var active = (await ledger.EnrollAsync(E("/data/active")))!;
        var start = _time.UtcNow;

        await ledger.ResolveAsync(old.Id, WatchResolution.Deleted, start.AddDays(-100));
        await ledger.ResolveAsync(fresh.Id, WatchResolution.Deleted, start.AddDays(-10));
        await ledger.SetPinnedAsync(pinned.Id, true, start.AddDays(-400));
        await ledger.RecordCheckInAsync(active, new CheckInRecord(active.Id, start.AddDays(-200), CheckInVerdict.Quiet, ObservationSource.None, 0, null));
        await ledger.RecordCheckInAsync(active, new CheckInRecord(active.Id, start.AddDays(-5), CheckInVerdict.Quiet, ObservationSource.None, 0, null));
        await ledger.RecordHeartbeatAsync(start.AddDays(-500), ObservationSource.Heartbeat);
        await ledger.RecordHeartbeatAsync(start.AddDays(-5), ObservationSource.Heartbeat);
        await ledger.RecordOwnActivityAsync(start.AddDays(-500), start.AddDays(-500).AddMinutes(1), "scan");

        var removed = await ledger.PurgeAsync(start, options);

        Assert.Equal(4, removed); // the old finished entry, one old check-in, one old heartbeat, one old own-activity row
        var left = (await ledger.ListAsync(WatchListFilter.All)).Select(e => e.DisplayPath).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { "/data/active", "/data/fresh", "/data/pinned" }, left);
        Assert.Single(await ledger.GetCheckInsAsync(active.Id, 10));
        Assert.Single(await ledger.GetHeartbeatDaysAsync(start.AddDays(-1000)));
        Assert.Empty(await ledger.GetOwnActivityAsync(start.AddDays(-1000)));
    }

    [Fact]
    public async Task RunLock_ExcludesAnotherOwnerUntilReleasedOrExpired()
    {
        var ledger = NewLedger();
        var other = NewLedger();
        var ttl = TimeSpan.FromMinutes(5);

        Assert.True(await ledger.TryAcquireRunLockAsync("ui:1", ttl, _time.UtcNow));
        Assert.False(await other.TryAcquireRunLockAsync("headless:2", ttl, _time.UtcNow));
        Assert.True(await ledger.TryAcquireRunLockAsync("ui:1", ttl, _time.UtcNow)); // the holder may renew

        _time.Advance(TimeSpan.FromMinutes(6)); // the holder crashed: the lock expires by itself
        Assert.True(await other.TryAcquireRunLockAsync("headless:2", ttl, _time.UtcNow));

        await ledger.ReleaseRunLockAsync("ui:1"); // releasing somebody else's lock is a no-op
        Assert.False(await ledger.TryAcquireRunLockAsync("ui:1", ttl, _time.UtcNow));
        await other.ReleaseRunLockAsync("headless:2");
        Assert.True(await ledger.TryAcquireRunLockAsync("ui:1", ttl, _time.UtcNow));
    }

    [Fact]
    public async Task ClearAll_RemovesEverythingButTheConsent()
    {
        var ledger = NewLedger();
        var e = (await ledger.EnrollAsync(E("/data/a")))!;
        await ledger.RecordCheckInAsync(e, new CheckInRecord(e.Id, _time.UtcNow, CheckInVerdict.Quiet, ObservationSource.None, 0, null));
        await ledger.RecordHeartbeatAsync(_time.UtcNow, ObservationSource.Heartbeat);
        await ledger.RecordOwnActivityAsync(_time.UtcNow, _time.UtcNow, "scan");
        await ledger.AppendRunLogAsync(new RunLogRecord(_time.UtcNow, _time.UtcNow, "x", CheckInOutcome.Ok, 1, null));
        await ledger.SetVolumeCursorAsync(new VolumeCursor("C:", 7, 99, _time.UtcNow));
        await ledger.SetSettingAsync(ObservationSettingKeys.Enabled, "1");

        await ledger.ClearAllAsync();

        Assert.Empty(await ledger.ListAsync(WatchListFilter.All));
        Assert.Empty(await ledger.GetHeartbeatDaysAsync(DateTime.UnixEpoch));
        Assert.Empty(await ledger.GetOwnActivityAsync(DateTime.UnixEpoch));
        Assert.Null(await ledger.GetLastRunAsync());
        Assert.Null(await ledger.GetVolumeCursorAsync("C:"));
        Assert.Equal("1", await ledger.GetSettingAsync(ObservationSettingKeys.Enabled));
        Assert.Equal(0, RawLong("SELECT COUNT(*) FROM checkin"));
    }

    [Fact]
    public async Task SettingsCursorsAndRunLog_RoundTrip()
    {
        var ledger = NewLedger();

        await ledger.SetSettingAsync("k", "one");
        await ledger.SetSettingAsync("k", "two");
        await ledger.SetVolumeCursorAsync(new VolumeCursor("C:", 11, long.MaxValue - 5, _time.UtcNow));
        await ledger.SetVolumeCursorAsync(new VolumeCursor("C:", 12, 5, _time.UtcNow));
        await ledger.AppendRunLogAsync(new RunLogRecord(_time.UtcNow, _time.UtcNow.AddSeconds(3), "headless", CheckInOutcome.LockHeld, 0, "busy"));

        Assert.Equal("two", await ledger.GetSettingAsync("k"));
        Assert.Equal(new VolumeCursor("C:", 12, 5, _time.UtcNow), await ledger.GetVolumeCursorAsync("C:"));
        var last = (await ledger.GetLastRunAsync())!;
        Assert.Equal(CheckInOutcome.LockHeld, last.Outcome);
        Assert.Equal("busy", last.Detail);
    }

    [Fact]
    public async Task PinSnoozeAndAssessment_AreStoredOnTheEntry()
    {
        var ledger = NewLedger();
        var e = (await ledger.EnrollAsync(E("/data/a")))!;

        Assert.True(await ledger.SnoozeAsync(e.Id, _time.UtcNow.AddDays(30)));
        Assert.True(await ledger.UpdateAssessmentAsync(e.Id, CleanupTier.Likely, 0.83, "reasons", _time.UtcNow));

        var read = (await ledger.GetAsync(e.Id))!;
        Assert.Equal(_time.UtcNow.AddDays(30), read.SnoozeUntilUtc);
        Assert.Equal(_time.UtcNow, read.SnoozedAtUtc);
        Assert.Equal(CleanupTier.Likely, read.LastTier);
        Assert.Equal(0.83, read.LastScore!.Value, 6);
        Assert.Equal("reasons", read.LastReasons);

        Assert.True(await ledger.SnoozeAsync(e.Id, null));
        var unsnoozed = (await ledger.GetAsync(e.Id))!;
        Assert.Null(unsnoozed.SnoozeUntilUtc);
        Assert.Null(unsnoozed.SnoozedAtUtc);
    }

    [Fact]
    public async Task TheLookup_ListsWatchedAndPinnedPaths_AndFindsOverlaps()
    {
        var ledger = NewLedger();
        await ledger.EnrollAsync(E("/data/apps/tool"));
        var pinned = (await ledger.EnrollAsync(E("/data/pinned")))!;
        await ledger.SetPinnedAsync(pinned.Id, true, _time.UtcNow);
        var gone = (await ledger.EnrollAsync(E("/data/gone")))!;
        await ledger.ResolveAsync(gone.Id, WatchResolution.Deleted, _time.UtcNow);

        var lookup = await ledger.LoadLookupAsync();

        Assert.True(lookup.TryGet("/DATA/apps/tool/", out var summary));
        Assert.Equal(WatchState.Watching, summary.State);
        Assert.True(lookup.TryGet("/data/pinned", out var pin));
        Assert.True(pin.Pinned);
        Assert.False(lookup.TryGet("/data/gone", out _));
        Assert.Equal(1, lookup.ActiveCount);
        Assert.True(lookup.OverlapsExisting("/data/apps"));            // contains a watched path
        Assert.True(lookup.OverlapsExisting("/data/apps/tool/sub"));   // inside one
        Assert.True(lookup.OverlapsExisting("/data/apps/tool"));       // equal
        Assert.False(lookup.OverlapsExisting("/data/apps/toolbox"));   // only a name prefix
        Assert.False(lookup.OverlapsExisting("/data/other"));
        Assert.False(lookup.OverlapsExisting("/data/gone"));

        var summaries = await ledger.GetSummariesAsync(new[] { ObservationKey.From("/data/apps/tool"), ObservationKey.From("/data/gone") });
        Assert.Single(summaries);
    }
}
