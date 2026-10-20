using System;
using System.Collections.Generic;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// Finds application data whose application is gone. A folder directly below AppData\Roaming / Local / LocalLow
/// (or ProgramData) qualifies when
///  1. no installed program, Program Files entry or Start Menu entry matches its (or its vendor's) name, AND
///  2. nothing inside was touched for months (a removed app can not write any more).
/// Because name matching is fuzzy the rule can only ever contribute moderate evidence - it yields "Review" /
/// "Likely", never "Safe", unless the content independently is disposable.
/// </summary>
public sealed class OrphanedAppDataRule : IAssessmentRule
{
    // Shared / OS owned names that look "unmatched" but are not leftovers of one application.
    private static readonly HashSet<string> NeverOrphan = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "packages", "programs", "temp", "tmp", "history", "comms", "connecteddevicesplatform",
        "publishers", "virtualstore", "apppatch", "common files", "package cache", "desktop", "cryptneturlcache",
        "nuget", "npm", "pip", "python", "node", "dotnet", ".net", "cargo", "go", "java", "oracle", "sun",
        "intel", "nvidia", "nvidia corporation", "amd", "realtek", "dell", "hp", "lenovo", "asus",
    };

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        var f = ctx.Facts;
        if (!ctx.IsDirectory || f is null || !ctx.Machine.HasInstalledAppInventory)
            return;
        if (!ctx.Machine.IsUnderAppDataRoot(ctx.Path, out var root))
            return;
        if (sink.InDisposableContext)
            return; // the cache signal is more specific

        var norm = PathKey.Normalize(ctx.Path);
        if (norm.Length <= root.Length)
            return;

        var rel = PathKey.Segments(norm[root.Length..]);
        if (rel.Length is < 1 or > 2)
            return;
        if (NeverOrphan.Contains(rel[0]))
            return;

        // Ownership: does any installed program correspond to the vendor folder or to the app folder?
        foreach (var name in rel)
        {
            if (ctx.Machine.MatchesInstalledApp(name, out var app))
            {
                sink.Add(ReasonCode.InstalledApplication, EvidenceGroup.Ownership, -2.5, 0.60,
                    $"'{name}' corresponds to the installed program '{app}'");
                return;
            }
        }

        // Activity: an app that is not installed but still writes here (portable app, dev tool) is not a leftover.
        if (!f.IsComplete)
            return;
        var dormantDays = (ctx.NowUtc - f.NewestActivityUtc).TotalDays;
        if (f.NewestActivityUtc == DateTime.MinValue || dormantDays < ctx.Options.OrphanMinDormantDays)
            return;

        bool roaming = root.EndsWith("Roaming", StringComparison.OrdinalIgnoreCase);
        double risk = roaming ? 0.45 : 0.25; // Roaming holds settings / licences / saves that follow the user
        double llr = dormantDays >= 365 ? 2.0 : 1.6;

        sink.Add(ReasonCode.OrphanedAppData, EvidenceGroup.Ownership, llr, risk,
            $"no installed program matches '{rel[0]}' and nothing changed for {OwnershipRule.Ago(dormantDays).Replace(" ago", string.Empty)}");
    }
}
