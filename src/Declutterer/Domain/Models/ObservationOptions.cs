using System;

namespace Declutterer.Domain.Models;

/// <summary>
/// Tunables of the observation feature. A singleton on purpose separate from <see cref="SmartCleanupOptions"/>:
/// the numbers are heuristic and are meant to be reviewed, not hidden in rules. Defaults are deliberately conservative.
/// </summary>
public sealed class ObservationOptions
{
    /// <summary>Master switch. Off until the user consents; mirrored in the ledger's settings table.</summary>
    public bool Enabled { get; set; }

    // ── how long an item has to stay quiet (covered days, not calendar days) ──────────────────
    public int AppDataDays { get; set; } = 28;
    public int GenericFolderDays { get; set; } = 45;
    public int ProjectFolderDays { get; set; } = 60;

    public int RequiredDaysFor(ObservationClass c) => c switch
    {
        ObservationClass.AppData => AppDataDays,
        ObservationClass.ProjectFolder => ProjectFolderDays,
        _ => GenericFolderDays,
    };

    // ── lifecycle ─────────────────────────────────────────────────────────────────────────────
    /// <summary>How long strong use keeps an item "alive" before it gets a fresh baseline.</summary>
    public int AliveCooldownDays { get; set; } = 30;
    /// <summary>How long a completed quiet window stays valid before the item returns to Watching with a fresh baseline.</summary>
    public int QuietValidityDays { get; set; } = 30;
    /// <summary>A weak signal this recent stops quiet evidence from being emitted.</summary>
    public int WeakCooldownDays { get; set; } = 14;
    /// <summary>After this many returns from Alive to Watching the item is Retired.</summary>
    public int MaxAliveResets { get; set; } = 3;
    /// <summary>This many consecutive inconclusive check-ins make an item Unobservable.</summary>
    public int MaxInconclusiveAttempts { get; set; } = 6;
    /// <summary>
    /// Calendar days beyond this factor times the required days without reaching the required covered days count as
    /// "coverage too poor" (the machine is rarely on): every further check-in is inconclusive.
    /// </summary>
    public double CoverageGraceFactor { get; set; } = 3.0;

    // ── who is eligible ───────────────────────────────────────────────────────────────────────
    /// <summary>Size threshold used to prioritize larger watch suggestions when the active-watch capacity is limited.</summary>
    public long ObserveMinBytes { get; set; } = 16L * 1024 * 1024;
    /// <summary>Upper bound for the number of active entries.</summary>
    public int MaxWatchItems { get; set; } = 200;
    /// <summary>A folder whose share of documents / media / databases reaches this is never observed.</summary>
    public double ValuableShareLimit { get; set; } = 0.25;

    // ── evidence strengths (heuristic log-odds, like the rule table in SmartSelect.md) ───────
    public double ActiveLlr { get; set; } = -3.0;
    public double ActiveRisk { get; set; } = 0.5;
    public double WeakLlr { get; set; } = -0.6;
    public double WeakRisk { get; set; } = 0.1;
    /// <summary>Quiet evidence is bounded: it can corroborate, it can not carry an item on its own.</summary>
    public double QuietLlrCap { get; set; } = 1.4;
    /// <summary>Upper bound for the share of the "this is just stale" risk that completed quiet may remove.</summary>
    public double MaxRiskRelief { get; set; } = 0.5;

    // ── budgets for the metadata walk ─────────────────────────────────────────────────────────
    public int FingerprintMaxEntries { get; set; } = 300_000;
    public int FingerprintMaxDepth { get; set; } = 48;
    public int FingerprintChildHashCap { get; set; } = 2000;
    public TimeSpan CheckInTimeBudget { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan PerItemTimeBudget { get; set; } = TimeSpan.FromSeconds(20);
    public int CheckInMaxDegreeOfParallelism { get; set; } = 2;
    /// <summary>A crashed run can hold the cross-process run lock at most this long.</summary>
    public TimeSpan RunLockTtl { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>At most this many items are fully reassessed (engine run) per check-in run.</summary>
    public int MaxReassessmentsPerRun { get; set; } = 10;

    // ── weak access-time evidence ─────────────────────────────────────────────────────────────
    /// <summary>Access times inside our own activity windows (plus this padding) are discarded.</summary>
    public TimeSpan OwnActivityPadding { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Access times within this tolerance of the reference time are ignored.</summary>
    public TimeSpan AccessTimeTolerance { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>When this share of the files was accessed since the reference time it is a tree-wide burst (scan, backup).</summary>
    public double AccessBurstFraction { get; set; } = 0.5;
    public int AccessBurstMinEntries { get; set; } = 20;

    // ── retention ─────────────────────────────────────────────────────────────────────────────
    public int ResolvedRetentionDays { get; set; } = 90;
    public int CheckInRetentionDays { get; set; } = 180;
    public int HeartbeatRetentionDays { get; set; } = 400;
    public int SnoozeDays { get; set; } = 30;
}
