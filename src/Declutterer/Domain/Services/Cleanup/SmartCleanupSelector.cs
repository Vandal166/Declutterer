using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup;

public sealed class SelectionPlan
{
    public required IReadOnlyList<NodeSnapshot> Selected { get; init; }
    public long TotalBytes { get; init; }
    public int SafeCount { get; init; }
    public int LikelyCount { get; init; }
    public int ReviewCount { get; init; }
}

/// <summary>
/// Decides which assessed nodes get a ticked checkbox. Top-down: a qualifying node is taken as a whole and its
/// descendants are skipped (no double counting); a non-qualifying node is opened and its children are considered.
/// Whole-directory selection requires a complete safety scan, and a node is never taken if anything below it carries
/// a veto or a safety-scan block.
/// </summary>
public sealed class SmartCleanupSelector
{
    public static bool IsEligible(JunkAssessment a, SmartSelectMode mode, SmartCleanupOptions options)
    {
        if (a.HasHardKeep || !a.SelectionSafetyComplete || a.SelectionBlockReason is not null ||
            a.Tier is CleanupTier.Protected or CleanupTier.Keep)
            return false;

        return mode switch
        {
            SmartSelectMode.Conservative => a.Tier == CleanupTier.Safe,
            SmartSelectMode.Balanced => a.Tier >= CleanupTier.Likely,
            SmartSelectMode.Aggressive => a.Tier >= CleanupTier.Likely ||
                                          (a.Tier == CleanupTier.Review && a.Confidence >= options.AggressiveMinConfidence),
            _ => false,
        };
    }

    public SelectionPlan Plan(IReadOnlyList<NodeSnapshot> roots, IReadOnlyDictionary<NodeSnapshot, JunkAssessment> assessments,
        SmartSelectMode mode, SmartCleanupOptions options)
    {
        var vetoBelow = new Dictionary<NodeSnapshot, bool>();
        var selected = new List<NodeSnapshot>();

        bool IsVeto(NodeSnapshot n) =>
            assessments.TryGetValue(n, out var a) &&
            (a.HasHardKeep || a.Tier == CleanupTier.Protected || a.SelectionBlockReason is not null);

        // true if n or any descendant carries a veto
        bool ContainsVeto(NodeSnapshot n)
        {
            if (vetoBelow.TryGetValue(n, out var cached))
                return cached;
            bool result = IsVeto(n) || n.Children.Any(ContainsVeto);
            vetoBelow[n] = result;
            return result;
        }

        void Visit(NodeSnapshot n)
        {
            if (!n.IsRoot && assessments.TryGetValue(n, out var a) && IsEligible(a, mode, options) && !ContainsVeto(n))
            {
                selected.Add(n);
                return;
            }
            foreach (var child in n.Children)
                Visit(child);
        }

        foreach (var root in roots)
            Visit(root);

        int safe = 0, likely = 0, review = 0;
        foreach (var n in selected)
        {
            switch (assessments[n].Tier)
            {
                case CleanupTier.Safe: safe++; break;
                case CleanupTier.Likely: likely++; break;
                default: review++; break;
            }
        }

        return new SelectionPlan
        {
            Selected = selected,
            TotalBytes = selected.Sum(n => n.Size),
            SafeCount = safe,
            LikelyCount = likely,
            ReviewCount = review,
        };
    }
}
