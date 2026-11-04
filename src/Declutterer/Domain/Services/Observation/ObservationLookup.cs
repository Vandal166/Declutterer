using System;
using System.Collections.Generic;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

public sealed class ObservationLookup : IObservationLookup
{
    public static readonly ObservationLookup Empty = new(Array.Empty<WatchEntry>());

    private readonly Dictionary<string, ObservationSummary> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ancestorKeys = new(StringComparer.Ordinal);

    public ObservationLookup(IEnumerable<WatchEntry> entries)
    {
        foreach (var e in entries)
        {
            _byKey[e.Key] = e.ToSummary();
            if (e.IsActive)
                ActiveCount++;

            bool first = true;
            foreach (var ancestor in ObservationKey.SelfAndAncestors(e.DisplayPath))
            {
                if (first) { first = false; continue; }
                _ancestorKeys.Add(ancestor);
            }
        }
    }

    public int ActiveCount { get; }

    public bool TryGet(string path, out ObservationSummary summary) => _byKey.TryGetValue(ObservationKey.From(path), out summary!);

    public bool OverlapsExisting(string path)
    {
        var key = ObservationKey.From(path);
        if (_ancestorKeys.Contains(key))
            return true; // the path contains a watched path

        foreach (var k in ObservationKey.SelfAndAncestors(path))
        {
            if (_byKey.ContainsKey(k))
                return true; // equal to, or inside, a watched path
        }
        return false;
    }
}
