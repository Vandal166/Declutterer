using System;

namespace Declutterer.Domain.Models;

/// <summary>
/// Lifecycle of one watched item. Pinned and snoozed are flags on the entry, not states.
/// </summary>
public enum WatchState
{
    /// <summary>Being observed; the quiet clock is running.</summary>
    Watching = 0,
    /// <summary>Strong use was seen. Shows as "observed in use" until the cooldown ends.</summary>
    Alive = 1,
    /// <summary>The covered-day window was completed without strong use. Quiet evidence is valid until <c>ValidUntil</c>.</summary>
    QuietEligible = 2,
    /// <summary>Too many consecutive inconclusive check-ins. Not observed any more; the user may re-enroll.</summary>
    Unobservable = 3,
    /// <summary>Kept alive too often to be worth watching. The user may re-enroll.</summary>
    Retired = 4,
    /// <summary>Finished: see <see cref="WatchResolution"/>.</summary>
    Resolved = 5,
}

public enum WatchResolution
{
    None = 0,
    /// <summary>The item was deleted through Declutterer.</summary>
    Deleted = 1,
    /// <summary>The item disappeared (and could not be found again by its file identity).</summary>
    Missing = 2,
    /// <summary>The user stopped watching it.</summary>
    Declined = 3,
    /// <summary>The user pinned it: never suggest it again.</summary>
    Pinned = 4,
}

/// <summary>Decides how long an item has to stay quiet. Only folders are observed.</summary>
public enum ObservationClass
{
    AppData = 0,
    GenericFolder = 1,
    ProjectFolder = 2,
}

public enum UseStrength
{
    None = 0,
    /// <summary>Shell droppings, a move or rename, a last-access timestamp. Never resets the quiet clock.</summary>
    Weak = 1,
    /// <summary>Content changed, the user opened something inside, the item was replaced.</summary>
    Strong = 2,
}

/// <summary>Where a piece of observation evidence came from.</summary>
[Flags]
public enum ObservationSource
{
    None = 0,
    SnapshotDiff = 1,
    UsageArtifact = 2,
    AccessTime = 4,
    UsnJournal = 8,
    Heartbeat = 16,
}
