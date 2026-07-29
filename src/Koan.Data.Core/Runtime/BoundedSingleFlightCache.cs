namespace Koan.Data.Core.Runtime;

/// <summary>Host-owned finite cache that publishes one value per admitted key and forgets failed creation.</summary>
internal sealed class BoundedSingleFlightCache<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Lazy<TValue>> _entries;
    private readonly int _capacity;
    private readonly string _name;

    public BoundedSingleFlightCache(int capacity, string name, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _capacity = capacity;
        _name = name;
        _entries = new Dictionary<TKey, Lazy<TValue>>(comparer);
    }

    public TValue GetOrAdd(TKey key, Func<TValue> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        Lazy<TValue> entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                if (_entries.Count >= _capacity)
                {
                    throw new InvalidOperationException(
                        $"The host-owned {_name} reached its configured limit of {_capacity}. " +
                        "Reduce the admitted application shape or increase the corresponding Koan Data runtime bound.");
                }

                entry = new Lazy<TValue>(create, LazyThreadSafetyMode.ExecutionAndPublication);
                _entries.Add(key, entry);
            }
        }

        try
        {
            return entry.Value;
        }
        catch
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(key);
            }
            throw;
        }
    }

    internal int Count
    {
        get { lock (_gate) return _entries.Count; }
    }
}
