using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// Turns a bag of <see cref="Signal"/>s into the three numbers the user cares about.
///
///  • Junk likelihood  = sigmoid( logit(prior) + Σ per-group evidence ). Inside a group only the strongest
///    disposable and the strongest keep signal count, so correlated evidence is never double counted.
///  • Deletion risk    = noisy-OR over the per-group maximum risk, floored at 0.95 by any hard veto.
///  • Confidence       = evidence strength × agreement × diversity × coverage. Evidence from a single family
///    (e.g. "old") is capped and only lifted by corroboration from other families, so age alone can never
///    produce a confident verdict and "Safe" always needs a second, independent opinion.
/// </summary>
public sealed class AssessmentCombiner
{
    private static readonly int GroupCount = Enum.GetValues<EvidenceGroup>().Length;

    private readonly SmartCleanupOptions _options;

    public AssessmentCombiner(SmartCleanupOptions options) => _options = options;

    public JunkAssessment Combine(AssessmentContext ctx, IReadOnlyList<Signal> input)
    {
        var signals = new List<Signal>(input);
        var coverage = ComputeCoverage(ctx, signals);

        var pos = new double[GroupCount];
        var neg = new double[GroupCount];
        var risk = new double[GroupCount];
        bool hardKeep = false, isProtected = false;

        foreach (var s in signals)
        {
            int g = (int)s.Group;
            if (s.JunkLlr > pos[g]) pos[g] = s.JunkLlr;
            if (s.JunkLlr < neg[g]) neg[g] = s.JunkLlr;
            if (s.Risk > risk[g]) risk[g] = s.Risk;
            hardKeep |= s.HardKeep;
            isProtected |= s.Code == ReasonCode.SystemProtected;
        }

        // ── junk likelihood ────────────────────────────────────────────────
        double prior = Math.Clamp(_options.PriorJunk, 0.001, 0.999);
        double logit = Math.Log(prior / (1 - prior));
        var net = new double[GroupCount];
        for (int g = 0; g < GroupCount; g++)
        {
            net[g] = pos[g] + neg[g];
            logit += net[g];
        }
        double junk = 1.0 / (1.0 + Math.Exp(-logit));

        // ── deletion risk ──────────────────────────────────────────────────
        double keepProduct = 1.0;
        for (int g = 0; g < GroupCount; g++)
            keepProduct *= 1.0 - Math.Clamp(risk[g], 0, 1);
        double deletionRisk = 1.0 - keepProduct;

        if (hardKeep)
        {
            junk = Math.Min(junk, 0.10);
            deletionRisk = Math.Max(deletionRisk, 0.95);
        }
        if (isProtected)
        {
            junk = 0;
            deletionRisk = 1;
        }

        // ── confidence ─────────────────────────────────────────────────────
        double direction = junk >= 0.5 ? 1 : -1;
        double support = 0, oppose = 0, strongestSupport = 0;
        for (int g = 0; g < GroupCount; g++)
        {
            double d = direction * net[g];
            if (d > 0)
            {
                support += d;
                strongestSupport = Math.Max(strongestSupport, d);
            }
            else
            {
                oppose += -d;
            }
        }

        double strength = 1.0 - Math.Exp(-support / 2.5);
        double agreement = support + oppose > 0 ? support / (support + oppose) : 0;

        // Diversity: evidence from one family is capped (0.60, or 0.78 when that single family is overwhelming).
        // Every bit of corroboration from OTHER families lifts the cap smoothly, so "Safe" needs a second opinion
        // but does not fall off a cliff when that opinion is moderate.
        double secondary = support - strongestSupport;
        double baseDiversity = support <= 0 ? 0.30 : strongestSupport >= 4.0 ? 0.78 : 0.60;
        double diversity = baseDiversity + (1.0 - baseDiversity) * (1.0 - Math.Exp(-secondary / 1.2));
        double confidence = Math.Clamp(strength * agreement * diversity * coverage, 0, 1);
        if (hardKeep || isProtected)
            confidence = Math.Max(confidence, 0.9); // we are very sure we must NOT delete it

        var tier = isProtected ? CleanupTier.Protected
            : hardKeep ? CleanupTier.Keep
            : Classify(junk, deletionRisk, confidence);

        return new JunkAssessment
        {
            JunkScore = junk,
            DeletionRisk = deletionRisk,
            Confidence = confidence,
            Coverage = coverage,
            Tier = tier,
            Reasons = signals
                .OrderByDescending(s => s.HardKeep)
                .ThenByDescending(s => Math.Abs(s.JunkLlr))
                .ThenByDescending(s => s.Risk)
                .ToList(),
        };
    }

    private CleanupTier Classify(double junk, double risk, double confidence)
    {
        if (Meets(_options.Safe, junk, risk, confidence)) return CleanupTier.Safe;
        if (Meets(_options.Likely, junk, risk, confidence)) return CleanupTier.Likely;
        if (Meets(_options.Review, junk, risk, confidence)) return CleanupTier.Review;
        return CleanupTier.Keep;
    }

    private static bool Meets(TierThresholds t, double junk, double risk, double confidence) =>
        junk >= t.MinJunk && risk <= t.MaxRisk && confidence >= t.MinConfidence;

    /// <summary>
    /// Share of the item that was really looked at. Files are always complete. A directory that was not inspected
    /// gets 0.6, which makes "Safe" unreachable but still allows strong name-based verdicts to be "Likely".
    /// </summary>
    private static double ComputeCoverage(AssessmentContext ctx, List<Signal> signals)
    {
        if (!ctx.IsDirectory)
            return 1.0;

        var f = ctx.Facts;
        if (f is null)
            return 0.6;
        if (f.StoppedEarly)
            return 1.0; // the verdict "alive" is complete evidence

        double coverage = 1.0;
        if (f.Truncated)
        {
            double size = Math.Max(1, ctx.Node.Size);
            coverage = Math.Clamp(f.TotalBytes / size, 0.3, 0.95);
        }
        if (f.UnreadableCount > 0)
            coverage *= 0.9;

        if (coverage < 1.0)
        {
            signals.Add(new Signal(ReasonCode.IncompleteEvidence, EvidenceGroup.Coverage, 0, 0.3 * (1 - coverage),
                f.Truncated ? "only part of the folder could be inspected" : "some entries were unreadable"));
        }
        return coverage;
    }
}
