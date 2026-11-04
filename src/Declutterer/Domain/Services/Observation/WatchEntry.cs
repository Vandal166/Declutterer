using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// One row of the observation ledger. Immutable: the state machine returns a modified copy and the ledger persists it.
/// Only paths and aggregates are kept, never file contents or per-file access logs.
/// </summary>
public sealed record WatchEntry
{
    public long Id { get; init; }

    /// <summary>The key (see <see cref="ObservationKey"/>) the entry is unique under while it is not Resolved.</summary>
    public required string Key { get; init; }
    public required string DisplayPath { get; init; }
    public bool IsDirectory { get; init; } = true;
    /// <summary>Windows file identity (volume serial + file index) when available; lets a rename or move be recognised.</summary>
    public string? FileId { get; init; }

    public ObservationClass Class { get; init; }
    public WatchState State { get; init; } = WatchState.Watching;
    public WatchResolution Resolution { get; init; } = WatchResolution.None;

    public DateTime EnrolledUtc { get; init; }
    /// <summary>Start of the current quiet clock. Covered days are counted after this day.</summary>
    public DateTime QuietSinceUtc { get; init; }
    public int RequiredDays { get; init; }
    public int CoveredDays { get; init; }

    public DateTime? LastCheckInUtc { get; init; }
    public DateTime? LastActiveUtc { get; init; }
    public UseStrength LastActiveStrength { get; init; }
    public string? LastActiveDetail { get; init; }
    public DateTime? AliveUntilUtc { get; init; }
    public DateTime? ValidUntilUtc { get; init; }
    public int AliveResets { get; init; }
    public int InconclusiveStreak { get; init; }
    public ObservationSource Sources { get; init; }

    public long SizeAtEnrollment { get; init; }
    public CleanupTier? NominatedTier { get; init; }
    public double? NominatedScore { get; init; }
    public CleanupTier? LastTier { get; init; }
    public double? LastScore { get; init; }
    public string? LastReasons { get; init; }
    public DateTime? LastAssessedUtc { get; init; }

    public bool Pinned { get; init; }
    public DateTime? SnoozeUntilUtc { get; init; }
    /// <summary>When the current snooze began; its heartbeat days are not credited as quiet evidence.</summary>
    public DateTime? SnoozedAtUtc { get; init; }
    public DateTime? ResolvedUtc { get; init; }

    /// <summary>What the item looked like when the quiet clock started (or last absorbed a weak change).</summary>
    public Fingerprint? BaselineFingerprint { get; init; }
    /// <summary>What it looked like at the last conclusive check-in.</summary>
    public Fingerprint? LastFingerprint { get; init; }

    /// <summary>Watching, Alive or QuietEligible: the states that are checked in.</summary>
    public bool IsActive => State is WatchState.Watching or WatchState.Alive or WatchState.QuietEligible;

    public ObservationSummary ToSummary() => new(
        State, Class, CoveredDays, RequiredDays, EnrolledUtc, QuietSinceUtc, LastActiveUtc, LastActiveStrength, LastActiveDetail,
        AliveUntilUtc, ValidUntilUtc, InconclusiveStreak, Sources, Pinned, SnoozeUntilUtc, LastCheckInUtc, Resolution);
}
