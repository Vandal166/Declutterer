using System.Collections.Concurrent;

namespace Declutterer.Utilities.Helpers;

/// <summary>
/// Thread-safe HashSet for concurrent operations.
/// </summary>
internal class ConcurrentHashSet<T>
{
    private readonly ConcurrentDictionary<T, bool> _dictionary = new();

    public bool Add(T item)
    {
        return _dictionary.TryAdd(item, true);
    }

    public bool Contains(T item)
    {
        return _dictionary.ContainsKey(item);
    }

    public bool Remove(T item)
    {
        return _dictionary.TryRemove(item, out _);
    }
    
    public void Clear()
    {
        _dictionary.Clear();
    }
}