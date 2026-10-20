using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// The tree only contains what has been loaded, but junk usually hides one or two levels below what is visible
/// (node_modules inside a project, Cache inside an app folder). This picks the directories that are worth loading
/// one more level so that the next analysis pass can see inside:
///   - big enough to matter, not too deep below the scan root,
///   - not junk themselves (those are taken whole) and not vetoed for a reason that makes looking inside pointless
///     (running program, installed app, credentials, system-protected ...).
/// Projects (git repos) and container folders are deliberately allowed: their build output / contents are exactly
/// what we are looking for.
/// </summary>
public static class DeepeningPlanner
{
    private static readonly HashSet<ReasonCode> NoDive = new()
    {
        ReasonCode.RunningProcess,
        ReasonCode.StartupOrServiceReference,
        ReasonCode.InstalledApplication,
        ReasonCode.ContainsCredentials,
        ReasonCode.SystemProtected,
        ReasonCode.SensitiveLocation,
        ReasonCode.UserSettingsOrSaves,
    };

    public static IReadOnlyList<NodeSnapshot> SelectTargets(IEnumerable<NodeSnapshot> roots,
        IReadOnlyDictionary<NodeSnapshot, JunkAssessment> assessments, SmartCleanupOptions options)
    {
        var result = new List<NodeSnapshot>();

        foreach (var node in roots.SelectMany(r => r.SelfAndDescendants()))
        {
            if (node.IsRoot || !node.IsDirectory || !node.HasUnloadedChildren || node.Source is null)
                continue;
            if (node.Depth > options.DeepenMaxDepth || node.Size < options.DeepenMinBytes)
                continue;
            if (!assessments.TryGetValue(node, out var a))
                continue; // not assessed (inside a region that is taken whole)
            if (a.Tier >= CleanupTier.Likely)
                continue; // junk itself - no need to look inside
            if (a.Reasons.Any(r => r.HardKeep && NoDive.Contains(r.Code)))
                continue;

            result.Add(node);
        }

        return result
            .OrderByDescending(n => n.Size)
            .Take(Math.Max(0, options.DeepenMaxTargetsPerRound))
            .ToList();
    }
}
