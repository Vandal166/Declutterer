using System.Collections.ObjectModel;

namespace Declutterer.Benchmark.Common;

/// <summary>
/// Represents a dynamic unique data collection that provides notifications when items get added or removed, or when the whole list is refreshed.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public class ObservableHashSet<T> : ObservableCollection<T>
{
    protected override void InsertItem(int index, T item)
    {
        if (this.Contains(item))
            return;

        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, T item)
    {
        if (this.Contains(item))
            return;

        base.SetItem(index, item);
    }
}
