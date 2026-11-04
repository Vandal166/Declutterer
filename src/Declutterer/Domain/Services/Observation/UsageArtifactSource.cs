using System;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup.Rules;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// Strong evidence from what the OS recorded about the user: the shell's Recent items (the item, or a file inside it, was opened).
/// Only opens that are newer than the quiet clock start and the last strong use count, so one old open can not keep an item alive forever.
/// </summary>
public sealed class UsageArtifactSource : IActivitySource
{
    public ObservationSource Source => ObservationSource.UsageArtifact;

    public Task<ActivityReport> ObserveAsync(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken)
    {
        if (context.System.TryGetRecentOpen(context.CurrentPath, out var opened) &&
            opened > context.StrongReferenceUtc && opened <= context.NowUtc.AddHours(1))
        {
            var days = Math.Max(0, (context.NowUtc - opened).TotalDays);
            return Task.FromResult(new ActivityReport(Source, UseStrength.Strong, ActivityKind.RecentOpen, opened,
                $"opened {OwnershipRule.Ago(days)} (shell recent items)"));
        }

        return Task.FromResult(ActivityReport.Nothing(Source));
    }
}
