using System.Collections.Generic;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// The key a watched path is stored and looked up under: <see cref="PathKey.Normalize"/> upper-cased, so two spellings of
/// one path compare equal ordinally (the engine compares paths case-insensitively everywhere).
/// </summary>
public static class ObservationKey
{
    public static string From(string path) => PathKey.Normalize(path).ToUpperInvariant();

    /// <summary>The key of the path itself followed by the keys of every ancestor.</summary>
    public static IEnumerable<string> SelfAndAncestors(string path)
    {
        foreach (var p in PathKey.SelfAndAncestors(path))
            yield return p.ToUpperInvariant();
    }
}
