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
    /// <summary>Possibly junk, a human should look first.</summary>
    Review = 2,
    /// <summary>Very probably junk with low deletion risk.</summary>
    Likely = 3,
    /// <summary>Near-certain junk with negligible deletion risk.</summary>
    Safe = 4,
}

/// <summary>
/// The result of analysing one file or directory:
/// how likely it is junk, how risky deleting it would be, how confident we are, and why.
/// </summary>
public sealed class JunkAssessment
{
    /// <summary>0..1 - how likely is this item unnecessary?</summary>
    public required double JunkScore { get; init; }

    /// <summary>0..1 - how likely is deleting it to break something or lose valuable data?</summary>
    public required double DeletionRisk { get; init; }

    /// <summary>0..1 - how strong, diverse and consistent is the evidence?</summary>
    public required double Confidence { get; init; }

    /// <summary>0..1 - share of the item that was actually inspected.</summary>
    public double Coverage { get; init; } = 1.0;

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

    /// <summary>One-line summary, e.g. "Safe to delete (junk 97% / risk 3% / confidence 82%)".</summary>
    public string Summary =>
        $"{TierLabel} (junk {Pct(JunkScore)} / risk {Pct(DeletionRisk)} / confidence {Pct(Confidence)})";

    /// <summary>Same as <see cref="Explain"/> with default length; a property so it can be bound in XAML.</summary>
    public string Details => Explain();

    /// <summary>Multi-line explanation intended for tooltips and the review window.</summary>
    public string Explain(int maxReasons = 6)
    {
        var lines = new List<string> { Summary };
        foreach (var r in Reasons
                     .Where(r => r.JunkLlr != 0 || r.Risk >= 0.2 || r.HardKeep)
                     .Take(maxReasons))
        {
            var mark = r.HardKeep ? "!" : r.JunkLlr > 0 ? "+" : "-";
            lines.Add($" {mark} {r.Code.ToTitle()}: {r.Detail}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Pct(double v) => $"{(int)Math.Round(v * 100)}%";
}
