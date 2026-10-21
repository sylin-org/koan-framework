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

    public TValue GetOrAdd(TKey key, Func<TValue> create) => GetOrAdd(key, create, supersedes: null);

    /// <summary>
    /// Like <see cref="GetOrAdd(TKey, Func{TValue})"/>, and when <paramref name="key"/> is admitted it first forgets every
    /// entry <paramref name="supersedes"/> says the new key replaces (NK-170): an entry nothing can use any more must
    /// not hold a place in the bound. The check runs only when a key is admitted, never on a hit.
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TValue> create, Func<TKey, TKey, bool>? supersedes)
    {
        ArgumentNullException.ThrowIfNull(create);
        Pending entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                if (supersedes is not null)
                    foreach (var stale in _entries.Keys.Where(existing => supersedes(key, existing)).ToArray())
                        _entries.Remove(stale);
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

    /// <summary>
    /// Reads a value that has finished being created, without creating one. A creation in progress on this thread (a
    /// factory that re-enters the cache) reads as absent; one in progress on another thread is awaited.
    /// </summary>
    public bool TryGetCompleted(TKey key, out TValue value)
    {
        Pending? entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry))
            {
                value = default!;
                return false;
            }
        }

        return entry.TryRead(out value);
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

        public bool TryRead(out TValue value)
        {
            lock (_gate)
            {
                value = _value;
                return _factory is null;
            }
        }
    }
}
