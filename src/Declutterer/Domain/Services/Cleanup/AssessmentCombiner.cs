using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// Turns a bag of <see cref="Signal"/>s into the three numbers the user cares about.
///
///  • Junk score       = sigmoid( logit(prior) + Σ per-group evidence ). Inside a group only the strongest
///    disposable and the strongest keep signal count, so correlated evidence is never double counted.
///  • Deletion risk estimate = noisy-OR over the per-group maximum risk, floored at 0.95 by any hard veto.
///  • Evidence strength = evidence strength × agreement × diversity × coverage. Safe also requires positive
///    support from at least two configured evidence families.
///
/// Completed quiet observation (<see cref="ReasonCode.ObservedInactive"/>) is applied last and only where it is allowed to matter:
/// the item has to be Review or Likely WITHOUT it, or Keep with independent LongUntouched / OrphanedAppData evidence. Nothing may
/// veto it (hard keep, credentials, user documents), it may lift the tier by one step at most, and it may only relieve the
/// "merely stale" risk signals (never cloud sync, never a veto). A stale Keep can reach Review at most.
/// </summary>
public sealed class AssessmentCombiner
{
    private static readonly int GroupCount = Enum.GetValues<EvidenceGroup>().Length;

    /// <summary>The "this is only stale / not recently used" risks that completed quiet observation may relieve.</summary>
    private static readonly HashSet<ReasonCode> ReliefCodes = new()
    {
        ReasonCode.OrphanedAppData, ReasonCode.ProjectRoot, ReasonCode.RecentlyOpened, ReasonCode.RecentlyModified,
    };

    /// <summary>Evidence that makes quiet observation irrelevant: it must not help these items towards deletion at all.</summary>
    private static readonly HashSet<ReasonCode> QuietBlockers = new()
    {
        ReasonCode.ContainsUserDocuments, ReasonCode.ContainsCredentials, ReasonCode.SystemProtected, ReasonCode.SensitiveLocation,
        ReasonCode.BrowserProfile, ReasonCode.UserSettingsOrSaves, ReasonCode.VersionControlRepo,
    };

    private readonly SmartCleanupOptions _options;
    private readonly ObservationOptions _observation;

    public AssessmentCombiner(SmartCleanupOptions options, ObservationOptions? observation = null)
    {
        _options = options;
        _observation = observation ?? new ObservationOptions();
    }

    private static bool IsQuietEvidence(Signal s) => s.Code == ReasonCode.ObservedInactive && s.JunkLlr > 0;

    public JunkAssessment Combine(AssessmentContext ctx, IReadOnlyList<Signal> input)
    {
        var signals = new List<Signal>(input);
        var coverage = ComputeCoverage(ctx, signals);

        var quiet = signals.Where(IsQuietEvidence).ToList();
        if (quiet.Count == 0)
            return Evaluate(ctx, signals, coverage, relief: 0);

        // What would this item be without observation? Quiet evidence may only move an item that is already suspicious.
        var withoutQuiet = signals.Where(s => !IsQuietEvidence(s)).ToList();
        var baseline = Evaluate(ctx, withoutQuiet, coverage, relief: 0);

        Signal? blocker = null;
        foreach (var s in withoutQuiet)
        {
            if (s.HardKeep || QuietBlockers.Contains(s.Code))
            {
                blocker = s;
                break;
            }
        }

        bool staleKeep = baseline.Tier == CleanupTier.Keep &&
                         withoutQuiet.Any(s => s.Code is ReasonCode.LongUntouched or ReasonCode.OrphanedAppData);
        bool tierAllows = baseline.Tier is CleanupTier.Review or CleanupTier.Likely || staleKeep;
        if (staleKeep && withoutQuiet.Any(s => s.Code == ReasonCode.SyncedFolder))
            blocker ??= withoutQuiet.First(s => s.Code == ReasonCode.SyncedFolder);
        if (!tierAllows || blocker is not null)
        {
            var why = !tierAllows ? $"it is '{baseline.TierLabel}' without it" : $"blocked by \"{blocker!.Value.Code.ToTitle()}\"";
            return WithReasons(baseline, baseline.Reasons.Append(new Signal(ReasonCode.ObservedInactive, EvidenceGroup.Observation, 0, 0,
                $"stayed quiet, but this is not applied: {why}")).ToList());
        }

        // Relief is only for items that carry no cloud-sync risk (deleting there propagates to other devices).
        bool reliefAllowed = !withoutQuiet.Any(s => s.Code == ReasonCode.SyncedFolder);
        var relief = reliefAllowed ? Math.Clamp(quiet.Max(q => q.RiskRelief), 0, Math.Clamp(_observation.MaxRiskRelief, 0, 1)) : 0;

        var lifted = Evaluate(ctx, signals, coverage, relief);

        // never more than one step above what the item reached on its own
        var ceiling = staleKeep
            ? CleanupTier.Review
            : (CleanupTier)Math.Min((int)baseline.Tier + 1, (int)CleanupTier.Safe);
        return lifted.Tier > ceiling ? WithTier(lifted, ceiling) : lifted;
    }

    private static JunkAssessment WithReasons(JunkAssessment a, IReadOnlyList<Signal> reasons) => new()
    {
        JunkScore = a.JunkScore, DeletionRisk = a.DeletionRisk, Confidence = a.Confidence, Coverage = a.Coverage, Tier = a.Tier,
        Reasons = reasons, SelectionSafetyComplete = a.SelectionSafetyComplete, SelectionBlockReason = a.SelectionBlockReason,
    };

    private static JunkAssessment WithTier(JunkAssessment a, CleanupTier tier) => new()
    {
        JunkScore = a.JunkScore, DeletionRisk = a.DeletionRisk, Confidence = a.Confidence, Coverage = a.Coverage, Tier = tier,
        Reasons = a.Reasons, SelectionSafetyComplete = a.SelectionSafetyComplete, SelectionBlockReason = a.SelectionBlockReason,
    };

    private JunkAssessment Evaluate(AssessmentContext ctx, List<Signal> signals, double coverage, double relief)
    {
        var pos = new double[GroupCount];
        var neg = new double[GroupCount];
        var risk = new double[GroupCount];
        bool hardKeep = false, isProtected = false;

        foreach (var s in signals)
        {
            int g = (int)s.Group;
            if (s.JunkLlr > pos[g]) pos[g] = s.JunkLlr;
            if (s.JunkLlr < neg[g]) neg[g] = s.JunkLlr;
            var r = relief > 0 && ReliefCodes.Contains(s.Code) ? s.Risk * (1 - relief) : s.Risk;
            if (r > risk[g]) risk[g] = r;
            hardKeep |= s.HardKeep;
            isProtected |= s.Code == ReasonCode.SystemProtected;
        }

        // ── heuristic junk score ──────────────────────────────────────────
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
        int positiveFamilyCount = net.Count(n => n > 0);

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
            : Classify(junk, deletionRisk, confidence, positiveFamilyCount);

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

    private CleanupTier Classify(double junk, double risk, double confidence, int positiveFamilyCount)
    {
        if (positiveFamilyCount >= 2 && Meets(_options.Safe, junk, risk, confidence)) return CleanupTier.Safe;
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
