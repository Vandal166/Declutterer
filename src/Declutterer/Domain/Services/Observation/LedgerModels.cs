using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

public enum WatchListFilter
{
    /// <summary>Watching, Alive or QuietEligible: what check-ins work on.</summary>
    Active,
    /// <summary>Everything that influences assessments or nominations: all not-Resolved rows plus pinned ones.</summary>
    Lookup,
    /// <summary>Every row, including finished ones that are still within their retention.</summary>
    All,
}

/// <summary>How one item's check-in turned out.</summary>
public enum CheckInVerdict
{
    /// <summary>Compared conclusively and nothing changed.</summary>
    Quiet = 0,
    /// <summary>Strong use: content changed, something inside was opened, the item was replaced.</summary>
    StrongUse = 1,
    /// <summary>Only weak signals (shell files, a move or rename, a last-access time).</summary>
    WeakUse = 2,
    /// <summary>The item is gone and could not be found again.</summary>
    Missing = 3,
    /// <summary>No conclusion was possible (truncated or unreadable walk, no baseline, poor coverage, an error).</summary>
    Inconclusive = 4,
}

public enum CheckInOutcome
{
    Ok = 0,
    Disabled = 1,
    LockHeld = 2,
    LedgerUnavailable = 3,
    Error = 4,
    Cancelled = 5,
}

public sealed record CheckInRecord(long WatchItemId, DateTime Utc, CheckInVerdict Verdict, ObservationSource Sources, int CoveredDaysAdded, string? ChangeCounts);

public sealed record OwnActivityInterval(DateTime StartUtc, DateTime EndUtc, string Kind);

public sealed record RunLogRecord(DateTime StartedUtc, DateTime FinishedUtc, string Trigger, CheckInOutcome Outcome, int ItemsChecked, string? Detail);

public sealed record VolumeCursor(string Volume, long JournalId, long NextUsn, DateTime UpdatedUtc);

/// <summary>Raised when the ledger on disk was written by a newer version of the program. It is never opened for writing then.</summary>
public sealed class LedgerSchemaException : Exception
{
    public LedgerSchemaException(string message) : base(message) { }
}

public static class ObservationSettingKeys
{
    public const string Enabled = "enabled";
    public const string ConsentUtc = "consent_utc";
    public const string RunLock = "run_lock";
}
