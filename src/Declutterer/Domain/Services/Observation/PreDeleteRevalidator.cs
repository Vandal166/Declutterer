using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Serilog;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// For an item with observation history (any active watch entry equal to or under its path): a fresh metadata fingerprint must
/// match the one from the last check-in. Any difference, an incomplete walk, or a blocked path means the item is NOT deleted; a
/// changed entry is marked Alive, because the change is exactly the kind of use observation exists to notice. Items without
/// history are always allowed: the delete service's own path checks are unchanged.
/// Shell droppings (Thumbs.db, desktop.ini, .DS_Store) alone are not use and do not block.
/// </summary>
public sealed class PreDeleteRevalidator : IPreDeleteRevalidator
{
    private readonly IObservationLedger _ledger;
    private readonly FingerprintWalker _walker;
    private readonly ObservationOptions _options;
    private readonly Func<string, string?> _safetyGate;
    private readonly TimeProvider _time;

    public PreDeleteRevalidator(IObservationLedger ledger, FingerprintWalker walker, ObservationOptions options,
        Func<string, string?>? safetyGate = null, TimeProvider? timeProvider = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _walker = walker ?? throw new ArgumentNullException(nameof(walker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _safetyGate = safetyGate ?? JunkAssessmentEngine.DefaultSafetyGate;
        _time = timeProvider ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private async Task<System.Collections.Generic.List<WatchEntry>> HistoryOf(TreeNode item, CancellationToken ct)
    {
        var active = await _ledger.ListAsync(WatchListFilter.Active, ct).ConfigureAwait(false);
        return active.Where(e => PathKey.IsUnderOrEqual(e.DisplayPath, item.FullPath)).ToList();
    }

    public async Task<RevalidationResult> RevalidateAsync(TreeNode item, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_ledger.IsAvailable)
            return RevalidationResult.Ok;

        var history = await HistoryOf(item, cancellationToken).ConfigureAwait(false);
        if (history.Count == 0)
            return RevalidationResult.Ok;

        // the existing safety gate runs again for anything with history, right before it could be deleted
        var gate = _safetyGate(item.FullPath);
        if (!string.IsNullOrWhiteSpace(gate))
            return RevalidationResult.Blocked($"'{item.Name}' is protected: {gate}");

        foreach (var entry in history)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verdict = await CheckEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            if (!verdict.Allowed)
                return verdict;
        }

        return RevalidationResult.Ok;
    }

    private async Task<RevalidationResult> CheckEntryAsync(WatchEntry entry, CancellationToken ct)
    {
        var now = Now;
        var name = System.IO.Path.GetFileName(PathKey.Normalize(entry.DisplayPath));

        if (!System.IO.Directory.Exists(entry.DisplayPath))
        {
            await _ledger.ResolveAsync(entry.Id, WatchResolution.Missing, now, ct).ConfigureAwait(false);
            return RevalidationResult.Blocked($"'{name}' no longer exists, so what was assessed has changed; nothing was deleted");
        }

        var reference = entry.LastFingerprint ?? entry.BaselineFingerprint;
        var current = await Task.Run(() => _walker.Walk(entry.DisplayPath, new FingerprintWalkOptions
        {
            MaxEntries = _options.FingerprintMaxEntries,
            MaxDepth = _options.FingerprintMaxDepth,
            ChildHashCap = _options.FingerprintChildHashCap,
            DeadlineUtc = now + _options.PerItemTimeBudget,
        }, ct), ct).ConfigureAwait(false);

        if (reference is null || !reference.IsComplete || !current.IsComplete)
            return RevalidationResult.Blocked($"'{name}' could not be re-checked completely, so it was not deleted");

        var change = FingerprintComparer.Compare(reference, current);
        if (change.Identical)
            return RevalidationResult.Ok;
        if (change.ShellDroppingsOnly)
        {
            Log.Information("Only shell files changed in {Path} since the last check-in; not treated as use", entry.DisplayPath);
            return RevalidationResult.Ok;
        }

        var alive = entry with
        {
            State = WatchState.Alive,
            AliveUntilUtc = now.AddDays(_options.AliveCooldownDays),
            ValidUntilUtc = null,
            CoveredDays = 0,
            InconclusiveStreak = 0,
            LastActiveUtc = now,
            LastActiveStrength = UseStrength.Strong,
            LastActiveDetail = "changed since the last check-in (found while deleting): " + change.Describe(),
            LastFingerprint = current,
            LastCheckInUtc = now,
        };
        await _ledger.RecordCheckInAsync(alive, new CheckInRecord(entry.Id, now, CheckInVerdict.StrongUse, ObservationSource.SnapshotDiff, 0, null), ct)
            .ConfigureAwait(false);

        return RevalidationResult.Blocked($"'{name}' changed since it was last checked ({change.Describe()}); it is marked as in use and was not deleted");
    }

    public async Task MarkDeletedAsync(TreeNode item, CancellationToken cancellationToken = default)
    {
        if (!_ledger.IsAvailable)
            return;
        try
        {
            foreach (var entry in await HistoryOf(item, cancellationToken).ConfigureAwait(false))
                await _ledger.ResolveAsync(entry.Id, WatchResolution.Deleted, Now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Could not mark the watch entries of a deleted item as finished");
        }
    }
}
