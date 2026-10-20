using System;

namespace Declutterer.Domain.Models;

/// <summary>How aggressively Smart Select ticks checkboxes. Deletion always needs explicit confirmation.</summary>
public enum SmartSelectMode
{
    /// <summary>Only <see cref="CleanupTier.Safe"/> items.</summary>
    Conservative,
    /// <summary><see cref="CleanupTier.Safe"/> and <see cref="CleanupTier.Likely"/> items.</summary>
    Balanced,
    /// <summary>Also <see cref="CleanupTier.Review"/> items that have at least moderate confidence.</summary>
    Aggressive,
}

/// <summary>Joint thresholds an assessment has to meet to reach a tier.</summary>
public readonly record struct TierThresholds(double MinJunk, double MaxRisk, double MinConfidence);

/// <summary>
/// Tunables of the Smart Select engine. Defaults are deliberately conservative.
/// </summary>
public sealed class SmartCleanupOptions
{
    /// <summary>Baseline junk rate assumed by the heuristic scoring model; it is not an empirically measured rate.</summary>
    public double PriorJunk { get; set; } = 0.08;

    public TierThresholds Safe { get; set; } = new(0.90, 0.15, 0.75);
    public TierThresholds Likely { get; set; } = new(0.75, 0.35, 0.55);
    public TierThresholds Review { get; set; } = new(0.50, 0.60, 0.0);

    /// <summary>Minimum confidence for a Review-tier item to be ticked in <see cref="SmartSelectMode.Aggressive"/>.</summary>
    public double AggressiveMinConfidence { get; set; } = 0.40;

    /// <summary>Items touched within this window count as "alive".</summary>
    public int RecentWindowDays { get; set; } = 14;

    /// <summary>App data must be dormant this long before it is considered a leftover of a removed app.</summary>
    public int OrphanMinDormantDays { get; set; } = 180;

    /// <summary>Directories whose stage-A junk score is below this are not inspected on disk (they are clearly alive).</summary>
    public double DeepInspectMinJunk { get; set; } = 0.04;

    /// <summary>When false only metadata that is already loaded in the tree is used (fastest, least accurate).</summary>
    public bool EnableDeepInspection { get; set; } = true;

    /// <summary>Wall-clock budget for the disk inspection stage. Items not inspected in time can never reach Safe.</summary>
    public TimeSpan TimeBudget { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Upper bound of file system entries enumerated while inspecting one directory subtree.</summary>
    public int MaxEntriesPerDirectory { get; set; } = 300_000;

    public int MaxDepth { get; set; } = 48;

    /// <summary>Directories smaller than this are not worth loading deeper just to look for junk inside.</summary>
    public long DeepenMinBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>How many levels below a scan root Smart Select may load on its own to find junk.</summary>
    public int DeepenMaxDepth { get; set; } = 6;

    public int DeepenMaxTargetsPerRound { get; set; } = 150;
    public int DeepenMaxRounds { get; set; } = 3;

    /// <summary>
    /// Treat the creation time as activity. A copied / extracted file keeps its old write time but gets a new creation
    /// time, so ignoring it would classify freshly arrived files as "untouched for years".
    /// </summary>
    public bool UseCreationTime { get; set; } = true;

    public int MaxDegreeOfParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount);
}
