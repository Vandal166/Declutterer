using Declutterer.Domain.Models;

namespace Declutterer.Abstractions;

/// <summary>
/// Immutable, in-memory view of the ledger for one analysis run, so the rules (which run in parallel loops) never touch the
/// database. Lookups are by path and case-insensitive.
/// </summary>
public interface IObservationLookup
{
    /// <summary>The summary of the entry watching exactly this path.</summary>
    bool TryGet(string path, out ObservationSummary summary);

    /// <summary>True if the path equals, lies inside or contains a watched / pinned / snoozed / unobservable path.</summary>
    bool OverlapsExisting(string path);

    /// <summary>
    /// True if a pinned folder, or one that is snoozed right now, lies strictly below this path. Such a folder must never be selected
    /// as a whole: that would delete what the user asked to keep.
    /// </summary>
    bool ContainsKeptPath(string path);

    /// <summary>Entries that are checked in (Watching, Alive, QuietEligible). Counts against the watch limit.</summary>
    int ActiveCount { get; }
}
