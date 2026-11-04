using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// The primary source: compares what the item looks like now with the reference fingerprint (metadata only). Anything created,
/// modified or deleted is strong use; only shell droppings or a rename are weak. A walk that did not finish, or a missing
/// baseline, is inconclusive: it is never read as "unchanged".
/// </summary>
public sealed class SnapshotDiffSource : IActivitySource
{
    private const int MaxSiblingsToCompare = 500;

    private readonly IFileIdentityProvider _identity;

    public SnapshotDiffSource(IFileIdentityProvider identity)
    {
        _identity = identity;
    }

    public ObservationSource Source => ObservationSource.SnapshotDiff;

    public async Task<ActivityReport> ObserveAsync(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken)
    {
        var path = context.CurrentPath;
        string? newKey = null, newDisplay = null;
        bool moved = false;

        if (!Exists(entry, path))
        {
            var found = await Task.Run(() => TryFindRenamed(entry, context, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (found is null)
                return new ActivityReport(Source, UseStrength.None, ActivityKind.Missing, null, "no longer exists", IsMissing: true);

            path = found;
            context.RelocateTo(found);
            newKey = ObservationKey.From(found);
            newDisplay = found;
            moved = true;
        }

        var currentId = _identity.TryGetFileId(path);
        if (!moved && entry.FileId is not null && currentId is not null && !string.Equals(entry.FileId, currentId, StringComparison.Ordinal))
        {
            return new ActivityReport(Source, UseStrength.Strong, ActivityKind.Replaced, context.NowUtc,
                "was deleted and recreated (different file identity)", NewFileId: currentId);
        }

        var fingerprint = await context.GetFingerprintAsync(cancellationToken).ConfigureAwait(false);
        var reference = context.Reference;

        if (reference is null || !reference.IsComplete)
        {
            return new ActivityReport(Source, UseStrength.None, ActivityKind.NoBaseline, null, "no complete baseline to compare with yet",
                Conclusive: false, Fingerprint: fingerprint, NewKey: newKey, NewDisplayPath: newDisplay, NewFileId: moved ? currentId : null);
        }

        if (!fingerprint.IsComplete)
        {
            return new ActivityReport(Source, UseStrength.None, ActivityKind.Incomplete,
                null, "the metadata walk did not finish (size or time budget, or unreadable folders)", Conclusive: false,
                Fingerprint: fingerprint, NewKey: newKey, NewDisplayPath: newDisplay, NewFileId: moved ? currentId : null);
        }

        var change = FingerprintComparer.Compare(reference, fingerprint);

        if (change.Identical)
        {
            return moved
                ? new ActivityReport(Source, UseStrength.Weak, ActivityKind.Moved, context.NowUtc, $"{Declutterer.Domain.Services.Cleanup.Rules.ObservationRule.MovedDetailPrefix} to '{path}'", Changes: change,
                    Fingerprint: fingerprint, AdoptAsBaseline: true, NewKey: newKey, NewDisplayPath: newDisplay, NewFileId: currentId)
                : new ActivityReport(Source, UseStrength.None, ActivityKind.None, null, null, Changes: change, Fingerprint: fingerprint);
        }

        if (change.ShellDroppingsOnly)
        {
            return new ActivityReport(Source, UseStrength.Weak, ActivityKind.ShellDroppings, context.NowUtc, change.Describe(), Changes: change,
                Fingerprint: fingerprint, AdoptAsBaseline: true, NewKey: newKey, NewDisplayPath: newDisplay, NewFileId: moved ? currentId : null);
        }

        var at = fingerprint.NewestWriteUtc > context.Entry.QuietSinceUtc && fingerprint.NewestWriteUtc <= context.NowUtc
            ? fingerprint.NewestWriteUtc
            : context.NowUtc;
        return new ActivityReport(Source, UseStrength.Strong, ActivityKind.ContentChange, at, change.Describe(), Changes: change,
            Fingerprint: fingerprint, NewKey: newKey, NewDisplayPath: newDisplay, NewFileId: moved ? currentId : null);
    }

    private static bool Exists(WatchEntry entry, string path) => entry.IsDirectory ? Directory.Exists(path) : File.Exists(path);

    /// <summary>
    /// Looks for the same file identity among the siblings of the old location (a rename in place). A move to some other folder is
    /// not searched for: it reads as Missing, which is the safe direction.
    /// </summary>
    private string? TryFindRenamed(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken)
    {
        if (!_identity.IsSupported || entry.FileId is null)
            return null;

        var parent = Path.GetDirectoryName(Declutterer.Domain.Services.Cleanup.PathKey.Normalize(entry.DisplayPath));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return null;

        try
        {
            int seen = 0;
            foreach (var candidate in Directory.EnumerateFileSystemEntries(parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++seen > MaxSiblingsToCompare || DateTime.UtcNow >= context.DeadlineUtc)
                    return null;
                if (entry.IsDirectory != Directory.Exists(candidate))
                    continue;
                if (string.Equals(_identity.TryGetFileId(candidate), entry.FileId, StringComparison.Ordinal))
                    return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }
}
