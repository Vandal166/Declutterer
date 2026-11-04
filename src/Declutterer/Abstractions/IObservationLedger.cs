using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Abstractions;

/// <summary>Receives intervals during which Declutterer itself touched the file system, so its own reads are not mistaken for use.</summary>
public interface IOwnActivityRecorder
{
    Task RecordOwnActivityAsync(DateTime startUtc, DateTime endUtc, string kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistent record of what is being observed. Every operation is fail-soft: a missing, locked, corrupt or newer-schema
/// ledger yields empty results / false and a logged warning, never an exception and never evidence that could promote an item.
/// </summary>
public interface IObservationLedger : IOwnActivityRecorder
{
    string DatabasePath { get; }

    /// <summary>False once the ledger has been found unusable for good (for example written by a newer version).</summary>
    bool IsAvailable { get; }

    /// <summary>Inserts a new entry. Returns null if the path is already watched or pinned, or the ledger is unavailable.</summary>
    Task<WatchEntry?> EnrollAsync(WatchEntry entry, CancellationToken cancellationToken = default);

    Task<WatchEntry?> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatchEntry>> ListAsync(WatchListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Batch lookup by <see cref="ObservationKey"/>; only entries that influence assessments are returned.</summary>
    Task<IReadOnlyDictionary<string, ObservationSummary>> GetSummariesAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    Task<IObservationLookup> LoadLookupAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the state machine's result and the check-in row in one transaction. False if the entry was finished meanwhile.</summary>
    Task<bool> RecordCheckInAsync(WatchEntry updated, CheckInRecord checkIn, CancellationToken cancellationToken = default);

    /// <summary>Writes the mutable fields of an entry (not pin / snooze / assessment). False if the entry is gone or finished.</summary>
    Task<bool> UpdateAsync(WatchEntry updated, CancellationToken cancellationToken = default);

    Task<bool> UpdateAssessmentAsync(long id, CleanupTier tier, double score, string? reasons, DateTime utc, CancellationToken cancellationToken = default);

    Task<bool> ResolveAsync(long id, WatchResolution resolution, DateTime utc, CancellationToken cancellationToken = default);

    /// <summary>Pinning finishes the entry as Resolved(Pinned) and remembers it forever; unpinning forgets it.</summary>
    Task<bool> SetPinnedAsync(long id, bool pinned, DateTime utc, CancellationToken cancellationToken = default);

    Task<bool> SnoozeAsync(long id, DateTime? untilUtc, CancellationToken cancellationToken = default);

    Task RecordHeartbeatAsync(DateTime dayUtc, ObservationSource source, CancellationToken cancellationToken = default);

    /// <summary>UTC dates (time of day zero) on which the machine was proven to be in use, from <paramref name="sinceDayUtc"/> on.</summary>
    Task<IReadOnlyList<DateTime>> GetHeartbeatDaysAsync(DateTime sinceDayUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OwnActivityInterval>> GetOwnActivityAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CheckInRecord>> GetCheckInsAsync(long watchItemId, int take, CancellationToken cancellationToken = default);

    Task<VolumeCursor?> GetVolumeCursorAsync(string volume, CancellationToken cancellationToken = default);

    Task SetVolumeCursorAsync(VolumeCursor cursor, CancellationToken cancellationToken = default);

    Task AppendRunLogAsync(RunLogRecord record, CancellationToken cancellationToken = default);

    Task<RunLogRecord?> GetLastRunAsync(CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);

    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Cross-process mutual exclusion for check-in runs. A crashed holder only blocks until <paramref name="ttl"/> has passed.</summary>
    Task<bool> TryAcquireRunLockAsync(string owner, TimeSpan ttl, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task ReleaseRunLockAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>Deletes finished entries, old check-ins, heartbeats, own-activity intervals and run logs past their retention.</summary>
    Task<int> PurgeAsync(DateTime nowUtc, ObservationOptions options, CancellationToken cancellationToken = default);

    /// <summary>Removes everything the feature stored (settings, i.e. the consent, are kept).</summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
