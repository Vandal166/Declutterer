using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Serilog;

namespace Declutterer.Domain.Services.Observation;

public sealed record CheckInRunResult(
    CheckInOutcome Outcome,
    int ItemsChecked,
    int Alive,
    int QuietEligible,
    int Inconclusive,
    int Missing,
    int Deferred,
    int Reassessed,
    TimeSpan Duration,
    string? Detail)
{
    public static CheckInRunResult Of(CheckInOutcome outcome, string? detail = null) =>
        new(outcome, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, detail);
}

/// <summary>
/// One check-in run: record that the machine is in use today (the heartbeat), ask every activity source about every active item,
/// merge the answers, advance each item's state machine and persist the result. Cross-process safe (a lock in the ledger), bounded
/// in time, fair (the items checked longest ago go first), metadata-only, and it never throws for ledger or file system trouble.
/// </summary>
public sealed class ObservationCheckInService
{
    private readonly IObservationLedger _ledger;
    private readonly ISystemSnapshotProvider _snapshots;
    private readonly IReadOnlyList<IActivitySource> _sources;
    private readonly FingerprintWalker _walker;
    private readonly ObservationOptions _options;
    private readonly IObservationReassessor? _reassessor;
    private readonly IVolumeActivityProvider _volume;
    private readonly TimeProvider _time;

    public ObservationCheckInService(IObservationLedger ledger, ISystemSnapshotProvider snapshots, IEnumerable<IActivitySource> sources,
        FingerprintWalker walker, ObservationOptions options, IObservationReassessor? reassessor = null,
        IVolumeActivityProvider? volume = null, TimeProvider? timeProvider = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();
        _walker = walker ?? throw new ArgumentNullException(nameof(walker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _reassessor = reassessor;
        _volume = volume ?? new NullVolumeActivityProvider();
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<CheckInRunResult> RunAsync(string trigger, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return CheckInRunResult.Of(CheckInOutcome.Disabled);
        if (!_ledger.IsAvailable)
            return CheckInRunResult.Of(CheckInOutcome.LedgerUnavailable, "the observation ledger is unusable");

        var clock = Stopwatch.StartNew();
        var started = _time.GetUtcNow().UtcDateTime;
        var owner = $"{Environment.ProcessId}:{Guid.NewGuid():N}";

        if (!await _ledger.TryAcquireRunLockAsync(owner, _options.RunLockTtl, started, cancellationToken).ConfigureAwait(false))
        {
            var outcome = _ledger.IsAvailable ? CheckInOutcome.LockHeld : CheckInOutcome.LedgerUnavailable;
            await _ledger.AppendRunLogAsync(new RunLogRecord(started, started, trigger, outcome, 0, "another check-in is running"), CancellationToken.None).ConfigureAwait(false);
            return CheckInRunResult.Of(outcome, "another check-in is running or the ledger is unavailable");
        }

        int checkedCount = 0, alive = 0, quiet = 0, inconclusive = 0, missing = 0, deferred = 0, reassessed = 0;
        var outcomeKind = CheckInOutcome.Ok;
        string? detail = null;
        var becameQuiet = new List<long>();

        try
        {
            await _ledger.RecordHeartbeatAsync(started.Date, ObservationSource.Heartbeat, cancellationToken).ConfigureAwait(false);
            await RecordVolumeHeartbeatAsync(started, cancellationToken).ConfigureAwait(false);

            var entries = (await _ledger.ListAsync(WatchListFilter.Active, cancellationToken).ConfigureAwait(false))
                .Where(e => e.SnoozeUntilUtc is not { } snooze || snooze <= started)
                .OrderBy(e => e.LastCheckInUtc ?? DateTime.MinValue) // longest unchecked first: nobody starves
                .ToList();

            if (entries.Count > 0)
            {
                var machine = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
                var oldest = entries.Min(e => e.QuietSinceUtc).Date.AddDays(-1);
                var tracker = new CoverageTracker(await _ledger.GetHeartbeatDaysAsync(oldest, cancellationToken).ConfigureAwait(false));
                var ownActivity = await _ledger.GetOwnActivityAsync(oldest, cancellationToken).ConfigureAwait(false);
                var runDeadline = started + _options.CheckInTimeBudget;

                await Parallel.ForEachAsync(entries,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.CheckInMaxDegreeOfParallelism), CancellationToken = cancellationToken },
                    async (entry, token) =>
                    {
                        if (_time.GetUtcNow().UtcDateTime >= runDeadline)
                        {
                            Interlocked.Increment(ref deferred); // the next run starts with these (they were checked longest ago)
                            return;
                        }

                        var itemDeadline = Min(runDeadline, _time.GetUtcNow().UtcDateTime + _options.PerItemTimeBudget);
                        var outcome = await CheckOneAsync(entry, machine, tracker, ownActivity, started, itemDeadline, token).ConfigureAwait(false);
                        if (outcome is null)
                            return;

                        Interlocked.Increment(ref checkedCount);
                        switch (outcome.Kind)
                        {
                            case TransitionKind.BecameAlive or TransitionKind.ExtendedAlive: Interlocked.Increment(ref alive); break;
                            case TransitionKind.BecameQuietEligible:
                                Interlocked.Increment(ref quiet);
                                lock (becameQuiet) becameQuiet.Add(entry.Id);
                                break;
                            case TransitionKind.Resolved: Interlocked.Increment(ref missing); break;
                        }
                        if (outcome.Entry.InconclusiveStreak > 0)
                            Interlocked.Increment(ref inconclusive);
                    }).ConfigureAwait(false);

                reassessed = await ReassessAsync(becameQuiet, owner, cancellationToken).ConfigureAwait(false);
            }

            await _ledger.PurgeAsync(started, _options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcomeKind = CheckInOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Observation check-in run failed");
            outcomeKind = CheckInOutcome.Error;
            detail = ex.Message;
        }
        finally
        {
            var finished = _time.GetUtcNow().UtcDateTime;
            // our own reads must not be mistaken for use by later access-time comparisons
            await _ledger.RecordOwnActivityAsync(started, finished > started ? finished : started.AddSeconds(1), "checkin", CancellationToken.None).ConfigureAwait(false);
            await _ledger.AppendRunLogAsync(new RunLogRecord(started, finished, trigger, outcomeKind, checkedCount,
                detail ?? $"{alive} alive, {quiet} newly quiet, {inconclusive} inconclusive, {missing} gone, {deferred} deferred"), CancellationToken.None).ConfigureAwait(false);
            await _ledger.ReleaseRunLockAsync(owner, CancellationToken.None).ConfigureAwait(false);
        }

        clock.Stop();
        return new CheckInRunResult(outcomeKind, checkedCount, alive, quiet, inconclusive, missing, deferred, reassessed, clock.Elapsed, detail);
    }

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    private async Task RecordVolumeHeartbeatAsync(DateTime started, CancellationToken ct)
    {
        try
        {
            var report = await _volume.ReadAsync(started.AddDays(-_options.HeartbeatRetentionDays), ct).ConfigureAwait(false);
            if (!report.Available)
                return;

            if (report.JournalWrapped)
                Log.Information("The change journal was reset since the last read: days before the gap are not credited from it");

            foreach (var day in report.ActiveDaysUtc)
                await _ledger.RecordHeartbeatAsync(day, ObservationSource.UsnJournal, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "The volume activity provider failed; only check-in days count as heartbeat");
        }
    }

    private async Task<TransitionOutcome?> CheckOneAsync(WatchEntry entry, ISystemSnapshot machine, CoverageTracker tracker,
        IReadOnlyList<OwnActivityInterval> ownActivity, DateTime now, DateTime deadline, CancellationToken ct)
    {
        var context = new ObservationContext(entry, machine, _options, now, now, ownActivity, _walker, deadline);
        var reports = new List<ActivityReport>();

        foreach (var source in _sources)
        {
            try
            {
                reports.Add(await source.ObserveAsync(entry, context, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // an error is never "quiet": the comparison simply could not be made
                Log.Warning(ex, "Activity source {Source} failed for {Path}", source.Source, entry.DisplayPath);
                reports.Add(new ActivityReport(source.Source, UseStrength.None, ActivityKind.Error, null, "could not be observed: " + ex.Message, Conclusive: false));
            }
        }

        var coverage = tracker.Evaluate(entry, now, _options);
        var result = ActivityMerger.Merge(reports, coverage, ObservationSource.Heartbeat);
        var outcome = ObservationStateMachine.Transition(entry, result, now, _options);

        var record = new CheckInRecord(entry.Id, now, result.Verdict, result.Sources,
            Math.Max(0, outcome.Entry.CoveredDays - entry.CoveredDays), ChangeCounts(result.Changes));
        if (!await _ledger.RecordCheckInAsync(outcome.Entry, record, ct).ConfigureAwait(false))
            return null; // finished or removed meanwhile: nothing was written

        return outcome;
    }

    private static string? ChangeCounts(ChangeReport? changes) =>
        changes is { Comparable: true, Identical: false }
            ? $"{{\"created\":{changes.Created},\"modified\":{changes.Modified},\"deleted\":{changes.Deleted}}}"
            : null;

    private async Task<int> ReassessAsync(List<long> ids, string owner, CancellationToken ct)
    {
        if (_reassessor is null || ids.Count == 0)
            return 0;

        int done = 0;
        foreach (var id in ids.Take(_options.MaxReassessmentsPerRun))
        {
            ct.ThrowIfCancellationRequested();

            // A reassessment runs the whole engine (several time budgets), and the per-item budget of the main loop does not cover it:
            // ten of them can outlast the lock's TTL, which would let a second check-in (the scheduled one) start in parallel.
            // Renew the lock before each one, and stand aside if somebody else has taken it over in the meantime.
            if (!await _ledger.TryAcquireRunLockAsync(owner, _options.RunLockTtl, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false))
            {
                Log.Warning("Observation check-in lost its run lock; the remaining reassessments are left for the next run");
                break;
            }

            try
            {
                var fresh = await _ledger.GetAsync(id, ct).ConfigureAwait(false);
                if (fresh is { State: WatchState.QuietEligible } && await _reassessor.ReassessAsync(fresh, ct).ConfigureAwait(false) is not null)
                    done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Reassessment of watched item {Id} failed", id);
            }
        }
        return done;
    }
}
