using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Cleanup.Rules;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// Weak evidence from last-access times of files. Only used where the file system really maintains them, never inside a window in
/// which Declutterer itself was reading (or this very run), and never when most of the files were touched at once (an antivirus
/// scan, a backup, an indexer). It can only ever be weak: it never resets the quiet clock.
/// </summary>
public sealed class AccessTimeSource : IActivitySource
{
    public ObservationSource Source => ObservationSource.AccessTime;

    public async Task<ActivityReport> ObserveAsync(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken)
    {
        if (entry.State == WatchState.Alive)
            return ActivityReport.Nothing(Source); // already the strongest statement

        var fingerprint = await context.GetFingerprintAsync(cancellationToken).ConfigureAwait(false);
        if (!fingerprint.IsComplete || fingerprint.NewestAccessUtc == default)
            return ActivityReport.Nothing(Source);

        var options = context.Options;
        bool reliable = context.System.LastAccessTimeReliable ||
                        AccessTimeProbe.LooksMaintained(fingerprint.AccessSamples.Select(s => (s.AccessUtc, s.WriteUtc, s.CreatedUtc)), context.NowUtc);
        if (!reliable)
            return ActivityReport.Nothing(Source);

        var newest = fingerprint.NewestAccessUtc;
        if (newest <= context.AccessReferenceUtc + options.AccessTimeTolerance)
            return ActivityReport.Nothing(Source);
        if (context.IsOwnActivity(newest))
            return ActivityReport.Nothing(Source);

        if (fingerprint.FileCount >= options.AccessBurstMinEntries &&
            fingerprint.FilesAccessedSince / (double)fingerprint.FileCount >= options.AccessBurstFraction)
            return ActivityReport.Nothing(Source); // tree-wide burst: not a person

        var days = System.Math.Max(0, (context.NowUtc - newest).TotalDays);
        return new ActivityReport(Source, UseStrength.Weak, ActivityKind.AccessTime, newest, $"a file was last accessed {OwnershipRule.Ago(days)}");
    }
}
