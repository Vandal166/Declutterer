using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// "Who uses this?" - running programs, startup entries and services, installed applications, files the user opened
/// recently through the shell, and cloud-synced folders (where a deletion propagates to other devices).
/// This is the evidence that separates "old" from "useless".
/// </summary>
public sealed class OwnershipRule : IAssessmentRule
{
    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        var m = ctx.Machine;
        var path = ctx.Path;

        if (m.IsContainerRoot(path, out var container))
            sink.Add(ReasonCode.ContainerFolder, EvidenceGroup.Ownership, -5.0, 0.90,
                $"this is {container} itself - clean its contents instead", hardKeep: true);

        if (m.IsInRunningProcessPath(path))
            sink.Add(ReasonCode.RunningProcess, EvidenceGroup.Ownership, -6.0, 1.0, "a running program is executing from here", hardKeep: true);

        if (m.IsReferencedByStartupOrService(path))
            sink.Add(ReasonCode.StartupOrServiceReference, EvidenceGroup.Ownership, -5.0, 0.95, "referenced by a startup entry or Windows service", hardKeep: true);

        if (m.TryGetInstalledAppAt(path, out var app))
            sink.Add(ReasonCode.InstalledApplication, EvidenceGroup.Ownership, -4.0, 0.90, $"install location of '{app}'", hardKeep: true);

        if (m.TryGetRecentOpen(path, out var opened))
        {
            var days = (m.UtcNow - opened).TotalDays;
            if (days <= 90)
                sink.Add(ReasonCode.RecentlyOpened, EvidenceGroup.Ownership, -2.5, 0.60, $"opened {Ago(days)} (Windows recent items)");
            else if (days <= 365)
                sink.Add(ReasonCode.RecentlyOpened, EvidenceGroup.Ownership, -1.4, 0.35, $"opened {Ago(days)} (Windows recent items)");
        }

        if (m.IsInSyncedFolder(path, out var provider))
            sink.Add(ReasonCode.SyncedFolder, EvidenceGroup.Location, 0.0, 0.30, "deleting also removes it from other devices (cloud sync)");
    }

    internal static string Ago(double days) => days switch
    {
        < 1 => "today",
        < 60 => $"{(int)days} days ago",
        < 730 => $"{(int)(days / 30)} months ago",
        _ => $"{(int)(days / 365)} years ago",
    };
}
