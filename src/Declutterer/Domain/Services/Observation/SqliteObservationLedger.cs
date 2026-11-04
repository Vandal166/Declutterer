using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// SQLite implementation of the observation ledger (WAL, versioned schema, one short-lived connection per operation).
/// Writers use BEGIN IMMEDIATE so two processes (the UI and the scheduled headless run) serialise instead of failing.
/// Every failure mode degrades to "no observation evidence" with a warning: a corrupt file is moved aside and recreated, a
/// file from a newer version is never touched, a locked or unreadable one just yields nothing for that call.
/// </summary>
public sealed class SqliteObservationLedger : IObservationLedger
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;
    private const int SqliteConstraint = 19;

    private const string WatchColumns =
        "id, path_key, display_path, is_directory, file_id, class, state, resolution, enrolled_utc, quiet_since_utc, required_days, " +
        "covered_days, last_checkin_utc, last_active_utc, last_active_strength, last_active_detail, alive_until_utc, valid_until_utc, " +
        "alive_resets, inconclusive_streak, sources, size_at_enrollment, nominated_tier, nominated_score, last_tier, last_score, " +
        "last_reasons, last_assessed_utc, pinned, snooze_until_utc, resolved_utc, baseline_fingerprint, last_fingerprint, snoozed_at_utc";

    private readonly TimeProvider _time;
    private readonly object _initLock = new();
    private bool _initialized;
    private volatile bool _unavailable;

    public SqliteObservationLedger(string? databasePath = null, TimeProvider? timeProvider = null)
    {
        DatabasePath = databasePath ?? DefaultPath();
        _time = timeProvider ?? TimeProvider.System;
    }

    public string DatabasePath { get; }

    public bool IsAvailable => !_unavailable;

    /// <summary>Per-user, machine-local (not roamed): %LOCALAPPDATA%\Declutterer\observation\ledger.db on Windows.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Declutterer", "observation", "ledger.db");

    // ── plumbing ─────────────────────────────────────────────────────────────────────────────

    private static long Ticks(DateTime utc) => (utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime()).Ticks;
    private static object Ticks(DateTime? utc) => utc is { } v ? Ticks(v) : DBNull.Value;
    private static DateTime FromTicks(long ticks) => new(ticks, DateTimeKind.Utc);
    private static object Nullable(string? s) => s is null ? DBNull.Value : s;
    private static string Day(DateTime dayUtc) => dayUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private SqliteConnection Open()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, // a pooled connection would keep the file open and block moving a corrupt ledger aside
            DefaultTimeout = 10,
        }.ConnectionString;

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            Exec(connection, "PRAGMA busy_timeout = 8000; PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static int Exec(SqliteConnection c, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteNonQuery();
    }

    private static long Count(SqliteConnection c, string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(Scalar(c, sql, parameters) ?? 0L, CultureInfo.InvariantCulture);

    private static object? Scalar(SqliteConnection c, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        var result = cmd.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    private static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string Name, object Value)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(map(reader));
        return rows;
    }

    private static T InImmediateTransaction<T>(SqliteConnection c, Func<T> body)
    {
        Exec(c, "BEGIN IMMEDIATE");
        try
        {
            var result = body();
            Exec(c, "COMMIT");
            return result;
        }
        catch
        {
            try { Exec(c, "ROLLBACK"); } catch (SqliteException) { /* the original failure is the one worth reporting */ }
            throw;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            try
            {
                InitializeOnce();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
            {
                Log.Warning(ex, "Observation ledger {Path} is corrupt; moving it aside and starting a new one", DatabasePath);
                MoveCorruptFilesAside();
                InitializeOnce();
            }

            _initialized = true;
        }
    }

    private void InitializeOnce()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode = WAL;");
        Migrate(c);
    }

    private static void Migrate(SqliteConnection c)
    {
        if (Count(c, "PRAGMA user_version;") >= ObservationSchema.CurrentVersion)
        {
            RefuseNewerSchema(c);
            return;
        }

        InImmediateTransaction(c, () =>
        {
            // re-read inside the lock: another process may have migrated while we waited
            var version = (int)Count(c, "PRAGMA user_version;");
            if (version > ObservationSchema.CurrentVersion)
                throw new LedgerSchemaException($"The observation ledger has schema version {version}, this program understands up to {ObservationSchema.CurrentVersion}.");

            for (int target = version + 1; target <= ObservationSchema.CurrentVersion; target++)
            {
                Exec(c, ObservationSchema.Migrations[target - 1]);
                Exec(c, $"PRAGMA user_version = {target};");
            }
            return 0;
        });
    }

    private static void RefuseNewerSchema(SqliteConnection c)
    {
        var version = Count(c, "PRAGMA user_version;");
        if (version > ObservationSchema.CurrentVersion)
            throw new LedgerSchemaException($"The observation ledger has schema version {version}, this program understands up to {ObservationSchema.CurrentVersion}.");
    }

    private void MoveCorruptFilesAside()
    {
        SqliteConnection.ClearAllPools();
        var stamp = _time.GetUtcNow().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = DatabasePath + suffix;
            if (File.Exists(file))
                File.Move(file, $"{DatabasePath}.corrupt-{stamp}{suffix}", overwrite: true);
        }
    }

    /// <summary>Runs one ledger operation. Anything that goes wrong is logged and answered with <paramref name="fallback"/>.</summary>
    private T Run<T>(string what, Func<SqliteConnection, T> operation, T fallback)
    {
        if (_unavailable)
            return fallback;

        try
        {
            EnsureInitialized();
            using var c = Open();
            return operation(c);
        }
        catch (LedgerSchemaException ex)
        {
            _unavailable = true;
            Log.Warning(ex, "Observation ledger is unusable ({What}); observation evidence is disabled until the program is updated", what);
            return fallback;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            Log.Warning(ex, "Observation ledger operation {What} failed; continuing without observation evidence", what);
            return fallback;
        }
    }

    private Task<T> RunAsync<T>(string what, Func<SqliteConnection, T> operation, T fallback, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => Run(what, operation, fallback), ct);
    }

    // ── mapping ──────────────────────────────────────────────────────────────────────────────

    private static DateTime? ReadTime(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : FromTicks(r.GetInt64(i));
    private static string? ReadText(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static WatchEntry ReadEntry(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Key = r.GetString(1),
        DisplayPath = r.GetString(2),
        IsDirectory = r.GetInt64(3) != 0,
        FileId = ReadText(r, 4),
        Class = (ObservationClass)r.GetInt32(5),
        State = (WatchState)r.GetInt32(6),
        Resolution = (WatchResolution)r.GetInt32(7),
        EnrolledUtc = FromTicks(r.GetInt64(8)),
        QuietSinceUtc = FromTicks(r.GetInt64(9)),
        RequiredDays = r.GetInt32(10),
        CoveredDays = r.GetInt32(11),
        LastCheckInUtc = ReadTime(r, 12),
        LastActiveUtc = ReadTime(r, 13),
        LastActiveStrength = (UseStrength)r.GetInt32(14),
        LastActiveDetail = ReadText(r, 15),
        AliveUntilUtc = ReadTime(r, 16),
        ValidUntilUtc = ReadTime(r, 17),
        AliveResets = r.GetInt32(18),
        InconclusiveStreak = r.GetInt32(19),
        Sources = (ObservationSource)r.GetInt32(20),
        SizeAtEnrollment = r.GetInt64(21),
        NominatedTier = r.IsDBNull(22) ? null : (CleanupTier)r.GetInt32(22),
        NominatedScore = r.IsDBNull(23) ? null : r.GetDouble(23),
        LastTier = r.IsDBNull(24) ? null : (CleanupTier)r.GetInt32(24),
        LastScore = r.IsDBNull(25) ? null : r.GetDouble(25),
        LastReasons = ReadText(r, 26),
        LastAssessedUtc = ReadTime(r, 27),
        Pinned = r.GetInt64(28) != 0,
        SnoozeUntilUtc = ReadTime(r, 29),
        ResolvedUtc = ReadTime(r, 30),
        BaselineFingerprint = Fingerprint.FromJson(ReadText(r, 31)),
        LastFingerprint = Fingerprint.FromJson(ReadText(r, 32)),
        SnoozedAtUtc = ReadTime(r, 33),
    };

    private static (string, object)[] MutableParameters(WatchEntry e) => new (string, object)[]
    {
        ("$id", e.Id),
        ("$key", e.Key),
        ("$display", e.DisplayPath),
        ("$fileId", Nullable(e.FileId)),
        ("$state", (int)e.State),
        ("$resolution", (int)e.Resolution),
        ("$resolved", Ticks(e.ResolvedUtc)),
        ("$quietSince", Ticks(e.QuietSinceUtc)),
        ("$covered", e.CoveredDays),
        ("$lastCheckIn", Ticks(e.LastCheckInUtc)),
        ("$lastActive", Ticks(e.LastActiveUtc)),
        ("$lastStrength", (int)e.LastActiveStrength),
        ("$lastDetail", Nullable(e.LastActiveDetail)),
        ("$aliveUntil", Ticks(e.AliveUntilUtc)),
        ("$validUntil", Ticks(e.ValidUntilUtc)),
        ("$resets", e.AliveResets),
        ("$streak", e.InconclusiveStreak),
        ("$sources", (int)e.Sources),
        ("$baseline", Nullable(e.BaselineFingerprint?.ToJson())),
        ("$last", Nullable(e.LastFingerprint?.ToJson())),
        ("$snoozedAt", Ticks(e.SnoozedAtUtc)),
    };

    private static readonly string UpdateSql =
        "UPDATE watch_item SET path_key = $key, display_path = $display, file_id = $fileId, state = $state, resolution = $resolution, " +
        "resolved_utc = $resolved, quiet_since_utc = $quietSince, covered_days = $covered, last_checkin_utc = $lastCheckIn, " +
        "last_active_utc = $lastActive, last_active_strength = $lastStrength, last_active_detail = $lastDetail, alive_until_utc = $aliveUntil, " +
        "valid_until_utc = $validUntil, alive_resets = $resets, inconclusive_streak = $streak, sources = $sources, " +
        "baseline_fingerprint = $baseline, last_fingerprint = $last, snoozed_at_utc = $snoozedAt " +
        "WHERE id = $id AND state <> " + (int)WatchState.Resolved;

    private static bool TryUpdate(SqliteConnection c, WatchEntry e)
    {
        try
        {
            return Exec(c, UpdateSql, MutableParameters(e)) > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
        {
            Log.Warning(ex, "Observation entry {Id} could not be moved to {Path}: another entry already watches it", e.Id, e.DisplayPath);
            return false;
        }
    }

    // ── watch items ──────────────────────────────────────────────────────────────────────────

    public Task<WatchEntry?> EnrollAsync(WatchEntry e, CancellationToken ct = default) => RunAsync<WatchEntry?>("enroll", c =>
        InImmediateTransaction<WatchEntry?>(c, () =>
        {
            var existing = Count(c, "SELECT COUNT(*) FROM watch_item WHERE path_key = $k AND (state <> $resolved OR pinned = 1)",
                ("$k", e.Key), ("$resolved", (int)WatchState.Resolved));
            if (existing > 0)
                return null;

            Exec(c,
                "INSERT INTO watch_item (path_key, display_path, is_directory, file_id, class, state, resolution, enrolled_utc, quiet_since_utc, " +
                "required_days, covered_days, last_checkin_utc, last_active_utc, last_active_strength, last_active_detail, alive_until_utc, " +
                "valid_until_utc, alive_resets, inconclusive_streak, sources, size_at_enrollment, nominated_tier, nominated_score, last_tier, " +
                "last_score, last_reasons, last_assessed_utc, pinned, snooze_until_utc, resolved_utc, baseline_fingerprint, last_fingerprint, snoozed_at_utc) " +
                "VALUES ($key, $display, $isDir, $fileId, $class, $state, $resolution, $enrolled, $quietSince, $required, $covered, $lastCheckIn, " +
                "$lastActive, $lastStrength, $lastDetail, $aliveUntil, $validUntil, $resets, $streak, $sources, $size, $nomTier, $nomScore, $lastTier, " +
                "$lastScore, $lastReasons, $lastAssessed, $pinned, $snooze, $resolved, $baseline, $last, $snoozedAt)",
                ("$key", e.Key), ("$display", e.DisplayPath), ("$isDir", e.IsDirectory ? 1 : 0), ("$fileId", Nullable(e.FileId)),
                ("$class", (int)e.Class), ("$state", (int)e.State), ("$resolution", (int)e.Resolution), ("$enrolled", Ticks(e.EnrolledUtc)),
                ("$quietSince", Ticks(e.QuietSinceUtc)), ("$required", e.RequiredDays), ("$covered", e.CoveredDays),
                ("$lastCheckIn", Ticks(e.LastCheckInUtc)), ("$lastActive", Ticks(e.LastActiveUtc)), ("$lastStrength", (int)e.LastActiveStrength),
                ("$lastDetail", Nullable(e.LastActiveDetail)), ("$aliveUntil", Ticks(e.AliveUntilUtc)), ("$validUntil", Ticks(e.ValidUntilUtc)),
                ("$resets", e.AliveResets), ("$streak", e.InconclusiveStreak), ("$sources", (int)e.Sources), ("$size", e.SizeAtEnrollment),
                ("$nomTier", e.NominatedTier is { } nt ? (int)nt : DBNull.Value), ("$nomScore", e.NominatedScore is { } ns ? ns : DBNull.Value),
                ("$lastTier", e.LastTier is { } lt ? (int)lt : DBNull.Value), ("$lastScore", e.LastScore is { } ls ? ls : DBNull.Value),
                ("$lastReasons", Nullable(e.LastReasons)), ("$lastAssessed", Ticks(e.LastAssessedUtc)), ("$pinned", e.Pinned ? 1 : 0),
                ("$snooze", Ticks(e.SnoozeUntilUtc)), ("$resolved", Ticks(e.ResolvedUtc)),
                ("$baseline", Nullable(e.BaselineFingerprint?.ToJson())), ("$last", Nullable(e.LastFingerprint?.ToJson())),
                ("$snoozedAt", Ticks(e.SnoozedAtUtc)));

            var id = Count(c, "SELECT last_insert_rowid()");
            return e with { Id = id };
        }), null, ct);

    public Task<WatchEntry?> GetAsync(long id, CancellationToken ct = default) => RunAsync<WatchEntry?>("get", c =>
        Query(c, $"SELECT {WatchColumns} FROM watch_item WHERE id = $id", ReadEntry, ("$id", id)).FirstOrDefault(), null, ct);

    public Task<IReadOnlyList<WatchEntry>> ListAsync(WatchListFilter filter, CancellationToken ct = default) => RunAsync<IReadOnlyList<WatchEntry>>("list", c =>
    {
        var where = filter switch
        {
            WatchListFilter.Active => $"WHERE state IN ({(int)WatchState.Watching}, {(int)WatchState.Alive}, {(int)WatchState.QuietEligible})",
            WatchListFilter.Lookup => $"WHERE state <> {(int)WatchState.Resolved} OR pinned = 1",
            _ => string.Empty,
        };
        return Query(c, $"SELECT {WatchColumns} FROM watch_item {where} ORDER BY id", ReadEntry);
    }, Array.Empty<WatchEntry>(), ct);

    public async Task<IReadOnlyDictionary<string, ObservationSummary>> GetSummariesAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
        if (wanted.Count == 0)
            return new Dictionary<string, ObservationSummary>();

        var entries = await ListAsync(WatchListFilter.Lookup, ct).ConfigureAwait(false);
        return entries.Where(e => wanted.Contains(e.Key)).ToDictionary(e => e.Key, e => e.ToSummary(), StringComparer.Ordinal);
    }

    public async Task<IObservationLookup> LoadLookupAsync(CancellationToken ct = default)
    {
        var entries = await ListAsync(WatchListFilter.Lookup, ct).ConfigureAwait(false);
        return entries.Count == 0 ? ObservationLookup.Empty : new ObservationLookup(entries);
    }

    public Task<bool> RecordCheckInAsync(WatchEntry updated, CheckInRecord checkIn, CancellationToken ct = default) => RunAsync("check-in", c =>
        InImmediateTransaction(c, () =>
        {
            if (!TryUpdate(c, updated))
                return false;

            Exec(c, "INSERT INTO checkin (watch_item_id, utc, verdict, source_mask, covered_days_added, change_counts) " +
                    "VALUES ($item, $utc, $verdict, $mask, $added, $counts)",
                ("$item", checkIn.WatchItemId), ("$utc", Ticks(checkIn.Utc)), ("$verdict", (int)checkIn.Verdict),
                ("$mask", (int)checkIn.Sources), ("$added", checkIn.CoveredDaysAdded), ("$counts", Nullable(checkIn.ChangeCounts)));
            return true;
        }), false, ct);

    public Task<bool> UpdateAsync(WatchEntry updated, CancellationToken ct = default) => RunAsync("update",
        c => InImmediateTransaction(c, () => TryUpdate(c, updated)), false, ct);

    public Task<bool> UpdateAssessmentAsync(long id, CleanupTier tier, double score, string? reasons, DateTime utc, CancellationToken ct = default) =>
        RunAsync("update-assessment", c => Exec(c,
            "UPDATE watch_item SET last_tier = $tier, last_score = $score, last_reasons = $reasons, last_assessed_utc = $utc WHERE id = $id",
            ("$tier", (int)tier), ("$score", score), ("$reasons", Nullable(reasons)), ("$utc", Ticks(utc)), ("$id", id)) > 0, false, ct);

    public Task<bool> ResolveAsync(long id, WatchResolution resolution, DateTime utc, CancellationToken ct = default) => RunAsync("resolve", c => Exec(c,
        "UPDATE watch_item SET state = $resolved, resolution = $resolution, resolved_utc = $utc WHERE id = $id AND state <> $resolved",
        ("$resolved", (int)WatchState.Resolved), ("$resolution", (int)resolution), ("$utc", Ticks(utc)), ("$id", id)) > 0, false, ct);

    public Task<bool> SetPinnedAsync(long id, bool pinned, DateTime utc, CancellationToken ct = default) => RunAsync("pin", c =>
    {
        if (pinned)
        {
            return Exec(c,
                "UPDATE watch_item SET pinned = 1, state = $resolved, resolution = $pinnedResolution, resolved_utc = $utc WHERE id = $id AND state <> $resolved",
                ("$resolved", (int)WatchState.Resolved), ("$pinnedResolution", (int)WatchResolution.Pinned), ("$utc", Ticks(utc)), ("$id", id)) > 0;
        }

        return Exec(c, "DELETE FROM watch_item WHERE id = $id AND pinned = 1", ("$id", id)) > 0;
    }, false, ct);

    public Task<bool> SnoozeAsync(long id, DateTime? untilUtc, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        DateTime? snoozedAt = untilUtc is null ? null : now;
        return RunAsync<bool>("snooze", c => Exec(c,
            "UPDATE watch_item SET snooze_until_utc = $until, snoozed_at_utc = $snoozedAt, quiet_since_utc = $quietSince, " +
            "covered_days = CASE WHEN $until IS NULL THEN 0 ELSE covered_days END WHERE id = $id AND state <> $resolved",
            new (string Name, object Value)[]
            {
                ("$until", Ticks(untilUtc)), ("$snoozedAt", Ticks(snoozedAt)), ("$quietSince", Ticks(now)),
                ("$id", id), ("$resolved", (int)WatchState.Resolved),
            }) > 0, false, ct);
    }

    // ── heartbeat / own activity / check-ins ─────────────────────────────────────────────────

    public Task RecordHeartbeatAsync(DateTime dayUtc, ObservationSource source, CancellationToken ct = default) => RunAsync("heartbeat", c => Exec(c,
        "INSERT INTO covered_day (day, heartbeat_source) VALUES ($d, $s) " +
        "ON CONFLICT(day) DO UPDATE SET heartbeat_source = heartbeat_source | excluded.heartbeat_source",
        ("$d", Day(dayUtc)), ("$s", (int)source)), 0, ct);

    public Task<IReadOnlyList<DateTime>> GetHeartbeatDaysAsync(DateTime sinceDayUtc, CancellationToken ct = default) =>
        RunAsync<IReadOnlyList<DateTime>>("heartbeat-days", c => Query(c, "SELECT day FROM covered_day WHERE day >= $d ORDER BY day",
            r => DateTime.SpecifyKind(DateTime.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc),
            ("$d", Day(sinceDayUtc))), Array.Empty<DateTime>(), ct);

    public Task RecordOwnActivityAsync(DateTime startUtc, DateTime endUtc, string kind, CancellationToken ct = default) => RunAsync("own-activity", c => Exec(c,
        "INSERT INTO own_activity (start_utc, end_utc, kind) VALUES ($s, $e, $k)",
        ("$s", Ticks(startUtc)), ("$e", Ticks(endUtc < startUtc ? startUtc : endUtc)), ("$k", kind)), 0, ct);

    public Task<IReadOnlyList<OwnActivityInterval>> GetOwnActivityAsync(DateTime sinceUtc, CancellationToken ct = default) =>
        RunAsync<IReadOnlyList<OwnActivityInterval>>("own-activity-list", c => Query(c,
            "SELECT start_utc, end_utc, kind FROM own_activity WHERE end_utc >= $since ORDER BY start_utc",
            r => new OwnActivityInterval(FromTicks(r.GetInt64(0)), FromTicks(r.GetInt64(1)), r.GetString(2)), ("$since", Ticks(sinceUtc))),
            Array.Empty<OwnActivityInterval>(), ct);

    public Task<IReadOnlyList<CheckInRecord>> GetCheckInsAsync(long watchItemId, int take, CancellationToken ct = default) =>
        RunAsync<IReadOnlyList<CheckInRecord>>("checkins", c => Query(c,
            "SELECT watch_item_id, utc, verdict, source_mask, covered_days_added, change_counts FROM checkin WHERE watch_item_id = $id ORDER BY id DESC LIMIT $n",
            r => new CheckInRecord(r.GetInt64(0), FromTicks(r.GetInt64(1)), (CheckInVerdict)r.GetInt32(2), (ObservationSource)r.GetInt32(3), r.GetInt32(4), ReadText(r, 5)),
            ("$id", watchItemId), ("$n", take)), Array.Empty<CheckInRecord>(), ct);

    // ── cursors / run log / settings / lock ──────────────────────────────────────────────────

    public Task<VolumeCursor?> GetVolumeCursorAsync(string volume, CancellationToken ct = default) => RunAsync<VolumeCursor?>("cursor-get", c =>
        Query(c, "SELECT volume, journal_id, next_usn, updated_utc FROM volume_cursor WHERE volume = $v",
            r => new VolumeCursor(r.GetString(0), r.GetInt64(1), r.GetInt64(2), FromTicks(r.GetInt64(3))), ("$v", volume)).FirstOrDefault(), null, ct);

    public Task SetVolumeCursorAsync(VolumeCursor cursor, CancellationToken ct = default) => RunAsync("cursor-set", c => Exec(c,
        "INSERT INTO volume_cursor (volume, journal_id, next_usn, updated_utc) VALUES ($v, $j, $n, $u) " +
        "ON CONFLICT(volume) DO UPDATE SET journal_id = excluded.journal_id, next_usn = excluded.next_usn, updated_utc = excluded.updated_utc",
        ("$v", cursor.Volume), ("$j", cursor.JournalId), ("$n", cursor.NextUsn), ("$u", Ticks(cursor.UpdatedUtc))), 0, ct);

    public Task AppendRunLogAsync(RunLogRecord r, CancellationToken ct = default) => RunAsync("run-log", c => Exec(c,
        "INSERT INTO run_log (started_utc, finished_utc, trigger, outcome, items_checked, detail) VALUES ($s, $f, $t, $o, $n, $d)",
        ("$s", Ticks(r.StartedUtc)), ("$f", Ticks(r.FinishedUtc)), ("$t", r.Trigger), ("$o", (int)r.Outcome), ("$n", r.ItemsChecked),
        ("$d", Nullable(r.Detail))), 0, ct);

    public Task<RunLogRecord?> GetLastRunAsync(CancellationToken ct = default) => RunAsync<RunLogRecord?>("run-log-last", c =>
        Query(c, "SELECT started_utc, finished_utc, trigger, outcome, items_checked, detail FROM run_log ORDER BY id DESC LIMIT 1",
            r => new RunLogRecord(FromTicks(r.GetInt64(0)), FromTicks(r.GetInt64(1)), r.GetString(2), (CheckInOutcome)r.GetInt32(3), r.GetInt32(4), ReadText(r, 5))).FirstOrDefault(),
        null, ct);

    public Task<string?> GetSettingAsync(string key, CancellationToken ct = default) => RunAsync<string?>("setting-get", c =>
        Scalar(c, "SELECT value FROM settings WHERE key = $k", ("$k", key)) as string, null, ct);

    public Task SetSettingAsync(string key, string value, CancellationToken ct = default) => RunAsync("setting-set", c => Exec(c,
        "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
        ("$k", key), ("$v", value)), 0, ct);

    public Task<bool> TryAcquireRunLockAsync(string owner, TimeSpan ttl, DateTime nowUtc, CancellationToken ct = default) => RunAsync("lock-acquire", c =>
        InImmediateTransaction(c, () =>
        {
            var current = Scalar(c, "SELECT value FROM settings WHERE key = $k", ("$k", ObservationSettingKeys.RunLock)) as string;
            if (current is not null)
            {
                var parts = current.Split('|');
                if (parts.Length == 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiry) &&
                    expiry > Ticks(nowUtc) && !string.Equals(parts[0], owner, StringComparison.Ordinal))
                    return false; // somebody else holds a lock that has not expired
            }

            Exec(c, "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                ("$k", ObservationSettingKeys.RunLock), ("$v", owner + "|" + Ticks(nowUtc + ttl).ToString(CultureInfo.InvariantCulture)));
            return true;
        }), false, ct);

    public Task ReleaseRunLockAsync(string owner, CancellationToken ct = default) => RunAsync("lock-release", c =>
        InImmediateTransaction(c, () => Exec(c, "DELETE FROM settings WHERE key = $k AND value LIKE $prefix",
            ("$k", ObservationSettingKeys.RunLock), ("$prefix", owner + "|%"))), 0, ct);

    // ── retention ────────────────────────────────────────────────────────────────────────────

    public Task<int> PurgeAsync(DateTime nowUtc, ObservationOptions options, CancellationToken ct = default) => RunAsync("purge", c =>
        InImmediateTransaction(c, () =>
        {
            int removed = 0;
            // finished entries expire; pinned ones are the "never suggest this again" memory and stay until cleared
            removed += Exec(c, "DELETE FROM watch_item WHERE state = $resolved AND pinned = 0 AND COALESCE(resolved_utc, 0) < $cut",
                ("$resolved", (int)WatchState.Resolved), ("$cut", Ticks(nowUtc.AddDays(-options.ResolvedRetentionDays))));
            removed += Exec(c, "DELETE FROM checkin WHERE utc < $cut OR watch_item_id NOT IN (SELECT id FROM watch_item)",
                ("$cut", Ticks(nowUtc.AddDays(-options.CheckInRetentionDays))));
            removed += Exec(c, "DELETE FROM covered_day WHERE day < $cut", ("$cut", Day(nowUtc.AddDays(-options.HeartbeatRetentionDays))));
            removed += Exec(c, "DELETE FROM own_activity WHERE end_utc < $cut", ("$cut", Ticks(nowUtc.AddDays(-options.HeartbeatRetentionDays))));
            removed += Exec(c, "DELETE FROM run_log WHERE finished_utc < $cut", ("$cut", Ticks(nowUtc.AddDays(-options.CheckInRetentionDays))));
            return removed;
        }), 0, ct);

    public Task ClearAllAsync(CancellationToken ct = default) => RunAsync("clear", c =>
    {
        InImmediateTransaction(c, () =>
        {
            foreach (var table in new[] { "checkin", "watch_item", "covered_day", "volume_cursor", "own_activity", "run_log" })
                Exec(c, $"DELETE FROM {table}");
            Exec(c, "DELETE FROM settings WHERE key = $k", ("$k", ObservationSettingKeys.RunLock));
            return 0;
        });

        // really drop the pages, so "delete my data" means the file no longer contains it
        try
        {
            Exec(c, "VACUUM;");
            Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch (SqliteException ex)
        {
            Log.Warning(ex, "Observation ledger was cleared but could not be compacted");
        }
        return 0;
    }, 0, ct);
}
