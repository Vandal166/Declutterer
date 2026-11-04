using System;
using System.Collections.Generic;
using System.Linq;

namespace Declutterer.Domain.Models;

/// <summary>
/// Outcome tier of an assessment. Higher = more suitable for deletion.
/// </summary>
public enum CleanupTier
{
    /// <summary>System-critical location. Never selectable.</summary>
    Protected = 0,
    /// <summary>Evidence of use / value, or not enough evidence of junk.</summary>
    Keep = 1,
    /// <summary>Heuristic evidence suggests junk; a human should look first.</summary>
    Review = 2,
    /// <summary>Meets the heuristic thresholds for Likely with bounded deletion risk.</summary>
    Likely = 3,
    /// <summary>Meets the strongest heuristic junk and deletion-risk thresholds.</summary>
    Safe = 4,
}

/// <summary>
/// The result of analysing one file or directory. Numeric values are heuristic scores until validated against a
/// manually reviewed corpus.
/// </summary>
public sealed class JunkAssessment
{
    /// <summary>0..1 heuristic score for how strongly the evidence points to junk.</summary>
    public required double JunkScore { get; init; }

    /// <summary>0..1 heuristic estimate of the risk of harm from deletion.</summary>
    public required double DeletionRisk { get; init; }

    /// <summary>0..1 heuristic measure of evidence strength, diversity, and consistency.</summary>
    public required double Confidence { get; init; }

    /// <summary>0..1 - share of the item that was actually inspected.</summary>
    public double Coverage { get; init; } = 1.0;

    /// <summary>Whether a directory received a complete descendant safety scan, or a file passed its path safety check.</summary>
    public bool SelectionSafetyComplete { get; init; }

    /// <summary>Why Smart Select cannot auto-select this item. Human selection remains available.</summary>
    public string? SelectionBlockReason { get; init; }

    public required CleanupTier Tier { get; init; }

    /// <summary>The signals behind the verdict, strongest first.</summary>
    public required IReadOnlyList<Signal> Reasons { get; init; }

    /// <summary>True if any signal vetoes automatic selection.</summary>
    public bool HasHardKeep => Reasons.Any(r => r.HardKeep);

    public string TierLabel => Tier switch
    {
        CleanupTier.Safe => "Safe to delete",
        CleanupTier.Likely => "Likely junk",
        CleanupTier.Review => "Review",
        CleanupTier.Protected => "Protected",
        _ => "Keep",
    };

    /// <summary>One-line summary using heuristic scores rather than probability claims.</summary>
    public string Summary
    {
        get
        {
            var summary = $"{TierLabel} (junk score {Score(JunkScore)} / risk estimate {Score(DeletionRisk)} / evidence strength {Score(Confidence)})";
            return SelectionBlockReason is null ? summary : $"{summary}{Environment.NewLine}Not auto-selected: {SelectionBlockReason}";
        }
    }

    /// <summary>Same as <see cref="Explain"/> with default length; a property so it can be bound in XAML.</summary>
    public string Details => Explain();

    /// <summary>Multi-line explanation intended for tooltips and the review window.</summary>
    public string Explain(int maxReasons = 6)
    {
        var lines = new List<string> { Summary };
        foreach (var r in Reasons
                     .Where(r => r.JunkLlr != 0 || r.Risk >= 0.2 || r.HardKeep || r.Group == EvidenceGroup.Observation)
                     .Take(maxReasons))
        {
            var mark = r.HardKeep ? "!" : r.JunkLlr > 0 ? "+" : "-";
            lines.Add($" {mark} {r.Code.ToTitle()}: {r.Detail}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Score(double v) => $"{(int)Math.Round(v * 100)}/100";
}
