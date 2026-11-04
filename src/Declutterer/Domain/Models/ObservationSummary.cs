using System;

namespace Declutterer.Domain.Models;

/// <summary>
/// Immutable view of a watch entry that the assessment rules and the UI consume. It carries facts only; what they mean
/// for a verdict is decided by <c>ObservationRule</c> and the combiner.
/// </summary>
public sealed record ObservationSummary(
    WatchState State,
    ObservationClass Class,
    int CoveredDays,
    int RequiredDays,
    DateTime EnrolledUtc,
    DateTime QuietSinceUtc,
    DateTime? LastActiveUtc,
    UseStrength LastActiveStrength,
    string? LastActiveDetail,
    DateTime? AliveUntilUtc,
    DateTime? ValidUntilUtc,
    int InconclusiveAttempts,
    ObservationSource Sources,
    bool Pinned = false,
    DateTime? SnoozeUntilUtc = null,
    DateTime? LastCheckInUtc = null,
    WatchResolution Resolution = WatchResolution.None);
