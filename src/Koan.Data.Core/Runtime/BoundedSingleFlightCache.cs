namespace Koan.Data.Core.Runtime;

/// <summary>Host-owned finite cache that publishes one value per admitted key and forgets failed creation.</summary>
internal sealed class BoundedSingleFlightCache<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Pending> _entries;
    private readonly int _capacity;
    private readonly string _name;

    public BoundedSingleFlightCache(int capacity, string name, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _capacity = capacity;
        _name = name;
        _entries = new Dictionary<TKey, Pending>(comparer);
    }

    public TValue GetOrAdd(TKey key, Func<TValue> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        Pending entry;
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

                entry = new Pending(create);
                _entries.Add(key, entry);
            }
        }

        try
        {
            return entry.Value();
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

    // Lazy<TValue> would demand a public parameterless ctor of every TValue under ILC (a trim
    // annotation on Lazy's generic parameter) - a requirement these values never meet and the
    // factory-delegate form never uses. This holder keeps Lazy's exactly-once semantics: the
    // first caller runs the factory under the entry gate; racers that found the same entry block
    // on it and read the produced value.
    private sealed class Pending
    {
        private readonly object _gate = new();
        private Func<TValue>? _factory;
        private TValue _value = default!;

        public Pending(Func<TValue> create) => _factory = create;

        public TValue Value()
        {
            lock (_gate)
            {
                if (_factory is { } create)
                {
                    _value = create();
                    _factory = null;
                }
                return _value;
            }
        }
    }
}
