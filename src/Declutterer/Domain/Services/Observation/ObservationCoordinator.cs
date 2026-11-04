using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Serilog;

namespace Declutterer.Domain.Services.Observation;

/// <inheritdoc cref="IObservationCoordinator"/>
public sealed class ObservationCoordinator : IObservationCoordinator
{
    private readonly IObservationLedger _ledger;
    private readonly ObservationCheckInService _checkIn;
    private readonly IObservationReassessor? _reassessor;
    private readonly ISystemSnapshotProvider _snapshots;
    private readonly FingerprintWalker _walker;
    private readonly IFileIdentityProvider _identity;
    private readonly ObservationOptions _options;
    private readonly TimeProvider _time;

    public ObservationCoordinator(IObservationLedger ledger, ObservationCheckInService checkIn, ISystemSnapshotProvider snapshots,
        FingerprintWalker walker, IFileIdentityProvider identity, ObservationOptions options, IObservationReassessor? reassessor = null,
        TimeProvider? timeProvider = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _checkIn = checkIn ?? throw new ArgumentNullException(nameof(checkIn));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _walker = walker ?? throw new ArgumentNullException(nameof(walker));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _reassessor = reassessor;
        _time = timeProvider ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public bool IsEnabled => _options.Enabled;
    public ObservationOptions Options => _options;
    public bool IsLedgerAvailable => _ledger.IsAvailable;
    public string LedgerPath => _ledger.DatabasePath;

    // ── settings and consent ─────────────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _ledger.GetSettingAsync(ObservationSettingKeys.Enabled, cancellationToken).ConfigureAwait(false);
        _options.Enabled = stored == "1" && _ledger.IsAvailable;
    }

    public async Task<bool> HasConsentAsync(CancellationToken cancellationToken = default) =>
        await _ledger.GetSettingAsync(ObservationSettingKeys.ConsentUtc, cancellationToken).ConfigureAwait(false) is not null;

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (enabled && !_ledger.IsAvailable)
        {
            _options.Enabled = false;
            return;
        }

        if (enabled && !await HasConsentAsync(cancellationToken).ConfigureAwait(false))
            await _ledger.SetSettingAsync(ObservationSettingKeys.ConsentUtc, Now.ToString("O", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        await _ledger.SetSettingAsync(ObservationSettingKeys.Enabled, enabled ? "1" : "0", cancellationToken).ConfigureAwait(false);

        // The ledger is fail-soft: a write that did not stick must not leave the feature "on" in memory and off after a restart.
        var persisted = await _ledger.GetSettingAsync(ObservationSettingKeys.Enabled, cancellationToken).ConfigureAwait(false);
        _options.Enabled = enabled && persisted == "1";
        if (enabled && !_options.Enabled)
            Log.Warning("Observation could not be switched on: the setting could not be stored in the ledger at {Path}", _ledger.DatabasePath);
    }

    // ── enrolling ────────────────────────────────────────────────────────────────────────────

    public async Task<EnrollmentResult> EnrollAsync(IReadOnlyList<ObservationCandidate> candidates, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return EnrollmentResult.None("observation is switched off");
        if (!_ledger.IsAvailable)
            return EnrollmentResult.None("the observation ledger is not available");

        var started = Now;
        var skipped = new List<EnrollmentSkip>();
        int enrolled = 0, alive = 0;

        try
        {
            var machine = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var lookup = await _ledger.LoadLookupAsync(cancellationToken).ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (lookup.ActiveCount >= _options.MaxWatchItems)
                {
                    skipped.Add(new EnrollmentSkip(candidate.Path, $"the limit of {_options.MaxWatchItems} watched items is reached"));
                    continue;
                }
                if (lookup.OverlapsExisting(candidate.Path))
                {
                    skipped.Add(new EnrollmentSkip(candidate.Path, "already watched, pinned, or it contains or lies inside a watched folder"));
                    continue;
                }
                if (!Directory.Exists(candidate.Path))
                {
                    skipped.Add(new EnrollmentSkip(candidate.Path, "no longer exists"));
                    continue;
                }

                var baseline = await Task.Run(() => _walker.Walk(candidate.Path, new FingerprintWalkOptions
                {
                    MaxEntries = _options.FingerprintMaxEntries,
                    MaxDepth = _options.FingerprintMaxDepth,
                    ChildHashCap = _options.FingerprintChildHashCap,
                    DeadlineUtc = Now + _options.PerItemTimeBudget,
                }, cancellationToken), cancellationToken).ConfigureAwait(false);

                // an item that can not be fingerprinted completely can never be compared conclusively: do not pretend to watch it
                if (!baseline.IsComplete)
                {
                    skipped.Add(new EnrollmentSkip(candidate.Path, "too large or partly unreadable: it could not be fingerprinted completely"));
                    continue;
                }

                var entry = BuildEntry(candidate, baseline, machine);
                var saved = await _ledger.EnrollAsync(entry, cancellationToken).ConfigureAwait(false);
                if (saved is null)
                {
                    skipped.Add(new EnrollmentSkip(candidate.Path, "could not be saved (already watched?)"));
                    continue;
                }

                enrolled++;
                if (saved.State == WatchState.Alive)
                    alive++;
                lookup = await _ledger.LoadLookupAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // the baseline walks read the tree: later access-time comparisons must not mistake that for use
            await _ledger.RecordOwnActivityAsync(started, Now, "enroll", CancellationToken.None).ConfigureAwait(false);
        }

        return new EnrollmentResult(enrolled, alive, skipped);
    }

    /// <summary>
    /// Retroactive lookup first: if the item was used shortly before it was added (the shell's recent items, or its newest content change),
    /// it starts as Alive exactly as if that use had been observed. Otherwise the quiet clock starts now with the walk as baseline.
    /// </summary>
    private WatchEntry BuildEntry(ObservationCandidate candidate, Fingerprint baseline, ISystemSnapshot machine)
    {
        var now = Now;
        var required = _options.RequiredDaysFor(candidate.Class);

        DateTime? lastUse = candidate.LastKnownActivityUtc;
        if (machine.TryGetRecentOpen(candidate.Path, out var opened) && (lastUse is null || opened > lastUse))
            lastUse = opened;

        // use within the class window counts, but only while its alive cooldown has not run out: it then behaves exactly like observed use
        var window = TimeSpan.FromDays(Math.Min(required, _options.AliveCooldownDays));
        bool recentUse = lastUse is { } use && use <= now.AddHours(1) && now - use < window;

        return new WatchEntry
        {
            Key = ObservationKey.From(candidate.Path),
            DisplayPath = candidate.Path,
            IsDirectory = true,
            FileId = _identity.TryGetFileId(candidate.Path),
            Class = candidate.Class,
            State = recentUse ? WatchState.Alive : WatchState.Watching,
            EnrolledUtc = now,
            QuietSinceUtc = now,
            RequiredDays = required,
            AliveUntilUtc = recentUse ? lastUse!.Value.AddDays(_options.AliveCooldownDays) : null,
            LastActiveUtc = recentUse ? lastUse : null,
            LastActiveStrength = recentUse ? UseStrength.Strong : UseStrength.None,
            LastActiveDetail = recentUse ? "in use shortly before it was added to the watchlist" : null,
            Sources = ObservationSource.None,
            SizeAtEnrollment = candidate.Size,
            NominatedTier = candidate.Tier,
            NominatedScore = candidate.JunkScore,
            BaselineFingerprint = baseline,
            LastFingerprint = baseline,
        };
    }

    // ── check-ins ────────────────────────────────────────────────────────────────────────────

    public Task<CheckInRunResult> RunCheckInAsync(string trigger, CancellationToken cancellationToken = default) =>
        _checkIn.RunAsync(trigger, cancellationToken);

    public async Task StartupCatchUpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (_options.Enabled)
                await RunCheckInAsync("startup", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The startup observation check-in failed");
        }
    }

    // ── watchlist ────────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<WatchEntry>> GetWatchlistAsync(CancellationToken cancellationToken = default) =>
        _ledger.ListAsync(WatchListFilter.Lookup, cancellationToken);

    public Task<RunLogRecord?> GetLastRunAsync(CancellationToken cancellationToken = default) => _ledger.GetLastRunAsync(cancellationToken);

    public Task<bool> StopWatchingAsync(long id, CancellationToken cancellationToken = default) =>
        _ledger.ResolveAsync(id, WatchResolution.Declined, Now, cancellationToken);

    public Task<bool> PinAsync(long id, bool pinned, CancellationToken cancellationToken = default) =>
        _ledger.SetPinnedAsync(id, pinned, Now, cancellationToken);

    public Task<bool> SnoozeAsync(long id, CancellationToken cancellationToken = default) =>
        _ledger.SnoozeAsync(id, Now.AddDays(_options.SnoozeDays), cancellationToken);

    public async Task<bool> ReenrollAsync(long id, CancellationToken cancellationToken = default)
    {
        var entry = await _ledger.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.State is not (WatchState.Unobservable or WatchState.Retired) || !Directory.Exists(entry.DisplayPath))
            return false;

        var fresh = await Task.Run(() => _walker.Walk(entry.DisplayPath, new FingerprintWalkOptions
        {
            MaxEntries = _options.FingerprintMaxEntries,
            MaxDepth = _options.FingerprintMaxDepth,
            ChildHashCap = _options.FingerprintChildHashCap,
            DeadlineUtc = Now + _options.PerItemTimeBudget,
        }, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!fresh.IsComplete)
            return false;

        return await _ledger.UpdateAsync(entry with
        {
            State = WatchState.Watching,
            QuietSinceUtc = Now,
            CoveredDays = 0,
            InconclusiveStreak = 0,
            AliveResets = 0,
            AliveUntilUtc = null,
            ValidUntilUtc = null,
            LastActiveStrength = UseStrength.None,
            LastActiveUtc = null,
            LastActiveDetail = null,
            BaselineFingerprint = fresh,
            LastFingerprint = fresh,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReassessmentResult?> ReassessAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_reassessor is null)
            return null;
        var entry = await _ledger.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return entry is null ? null : await _reassessor.ReassessAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public Task ClearAllDataAsync(CancellationToken cancellationToken = default) => _ledger.ClearAllAsync(cancellationToken);
}
