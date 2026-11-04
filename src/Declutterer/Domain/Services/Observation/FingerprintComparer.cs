using System;

namespace Declutterer.Domain.Services.Observation;

public static class FingerprintComparer
{
    /// <summary>
    /// Compares two fingerprints. Anything incomplete is not comparable: the caller must treat that as inconclusive.
    /// "Shell droppings only" means the full hash differs, the hash without droppings and directory times is equal and the
    /// droppings themselves differ. A directory time change on its own (something created and removed a file) is a real change.
    /// </summary>
    public static ChangeReport Compare(Fingerprint baseline, Fingerprint current)
    {
        if (!baseline.IsComplete || !current.IsComplete)
            return ChangeReport.NotComparable;

        if (string.Equals(baseline.RootHash, current.RootHash, StringComparison.Ordinal))
            return new ChangeReport(true, true, false, 0, 0, 0);

        bool shellOnly = string.Equals(baseline.SignificantHash, current.SignificantHash, StringComparison.Ordinal) &&
                         !string.Equals(baseline.DroppingHash, current.DroppingHash, StringComparison.Ordinal);

        int created = 0, modified = 0, deleted = 0;
        foreach (var (name, hash) in current.Children)
        {
            if (!baseline.Children.TryGetValue(name, out var before))
                created++;
            else if (!string.Equals(before, hash, StringComparison.Ordinal))
                modified++;
        }
        foreach (var name in baseline.Children.Keys)
        {
            if (!current.Children.ContainsKey(name))
                deleted++;
        }

        // The root itself differs (its own time, or a capped child map): report at least one modification.
        if (created + modified + deleted == 0)
            modified = 1;

        return new ChangeReport(true, false, shellOnly, created, modified, deleted);
    }
}
