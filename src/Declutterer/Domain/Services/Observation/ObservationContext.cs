using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// Everything the activity sources of one check-in share about one item. The metadata walk is done at most once per path
/// and handed to every source that needs it.
/// </summary>
public sealed class ObservationContext
{
    private readonly FingerprintWalker _walker;
    private Task<Fingerprint>? _fingerprint;

    public ObservationContext(WatchEntry entry, ISystemSnapshot system, ObservationOptions options, DateTime nowUtc, DateTime runStartedUtc,
        IReadOnlyList<OwnActivityInterval> ownActivity, FingerprintWalker walker, DateTime deadlineUtc)
    {
        Entry = entry;
        System = system;
        Options = options;
        NowUtc = nowUtc;
        RunStartedUtc = runStartedUtc;
        OwnActivity = ownActivity;
        DeadlineUtc = deadlineUtc;
        _walker = walker;
        CurrentPath = entry.DisplayPath;
    }

    public WatchEntry Entry { get; }
    public ISystemSnapshot System { get; }
    public ObservationOptions Options { get; }
    public DateTime NowUtc { get; }
    public DateTime RunStartedUtc { get; }
    public DateTime DeadlineUtc { get; }
    public IReadOnlyList<OwnActivityInterval> OwnActivity { get; }

    /// <summary>Where the item is now. A source that finds it renamed moves this, and the walk then follows.</summary>
    public string CurrentPath { get; private set; }

    /// <summary>
    /// What "unchanged" is compared against: while the item is alive the previous check-in (it is expected to differ from the old
    /// baseline), otherwise the baseline.
    /// </summary>
    public Fingerprint? Reference => Entry.State == WatchState.Alive ? Entry.LastFingerprint ?? Entry.BaselineFingerprint : Entry.BaselineFingerprint;

    /// <summary>Strong evidence only counts when it is newer than this (the quiet clock start or the last strong use).</summary>
    public DateTime StrongReferenceUtc =>
        Entry.LastActiveStrength == UseStrength.Strong && Entry.LastActiveUtc is { } t && t > Entry.QuietSinceUtc ? t : Entry.QuietSinceUtc;

    /// <summary>Weak access times only count when they are newer than this.</summary>
    public DateTime AccessReferenceUtc => Entry.LastActiveUtc is { } t && t > Entry.QuietSinceUtc ? t : Entry.QuietSinceUtc;

    public void RelocateTo(string path)
    {
        CurrentPath = path;
        _fingerprint = null;
    }

    public Task<Fingerprint> GetFingerprintAsync(CancellationToken cancellationToken)
    {
        return _fingerprint ??= Task.Run(() => _walker.Walk(CurrentPath, new FingerprintWalkOptions
        {
            MaxEntries = Options.FingerprintMaxEntries,
            MaxDepth = Options.FingerprintMaxDepth,
            ChildHashCap = Options.FingerprintChildHashCap,
            DeadlineUtc = DeadlineUtc,
            AccessedSinceUtc = AccessReferenceUtc,
        }, cancellationToken), cancellationToken);
    }

    /// <summary>True if <paramref name="utc"/> falls into a window in which Declutterer itself was reading the file system.</summary>
    public bool IsOwnActivity(DateTime utc)
    {
        var pad = Options.OwnActivityPadding;
        if (utc >= RunStartedUtc - pad)
            return true;
        return OwnActivity.Any(i => utc >= i.StartUtc - pad && utc <= i.EndUtc + pad);
    }
}
