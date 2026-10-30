using System.Collections.Concurrent;

namespace Koan.Identity.Roles;

/// <summary>
/// A single-flight map of compiled state whose capacity sizes memory. Each invalidation replaces the key's entry with a
/// newer generation, so a compilation that began before the change never returns as current: its caller compiles
/// again. Admitting a key past capacity evicts one other entry, chosen by a clock sweep: an entry read since the hand
/// last passed it is spared once and its mark cleared, so the entry evicted is one nothing has read for a full turn —
/// an approximation of least recently used. A read that finds its entry takes no lock; admission takes one.
/// </summary>
internal sealed class CompiledStateMap<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Func<TKey, CancellationToken, Task<TValue>> _compile;
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

    // The clock: every admitted key once, in the order the hand reaches it. Guarded by locking the queue itself.
    private readonly Queue<TKey> _clock = new();
    private readonly HashSet<TKey> _onClock = new();

    public CompiledStateMap(int capacity, Func<TKey, CancellationToken, Task<TValue>> compile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _compile = compile;
    }

    /// <summary>How many entries the map holds now.</summary>
    public int Count => _entries.Count;

    /// <summary>Whether <paramref name="key"/> has an entry, without reading it.</summary>
    public bool Contains(TKey key) => _entries.ContainsKey(key);

    public async Task<TValue> Get(TKey key, CancellationToken ct)
    {
        while (true)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                var fresh = New(key, 0);
                entry = _entries.GetOrAdd(key, fresh);
                if (ReferenceEquals(entry, fresh)) Admit(key);
            }

            entry.Touch();
            try
            {
                var value = await entry.Value.Value.WaitAsync(ct).ConfigureAwait(false);
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) return value;
            }
            catch { _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry)); throw; }
            _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
        }
    }

    public void Invalidate(TKey key)
    {
        while (_entries.TryGetValue(key, out var current) &&
               !_entries.TryUpdate(key, New(key, current.Generation + 1), current)) { }
    }

    /// <summary>Invalidates every entry whose key matches <paramref name="matches"/>.</summary>
    public void InvalidateWhere(Func<TKey, bool> matches)
    {
        foreach (var key in _entries.Keys)
        {
            if (matches(key)) Invalidate(key);
        }
    }

    public void InvalidateAll() => InvalidateWhere(static _ => true);

    /// <summary>
    /// Puts a newly added key on the clock and, while the map is over capacity, evicts. The first turn of the hand
    /// spares each entry read since it last passed and clears its mark; the second evicts what it reaches. The key
    /// being admitted is never its own victim. The hand also drops keys whose entries a failed compilation removed,
    /// and turns while they would make the clock outgrow twice the capacity. The sweep is bounded by two turns, so a
    /// map that another admission is filling at the same moment may stay one entry over until that admission sweeps.
    /// </summary>
    private void Admit(TKey key)
    {
        lock (_clock)
        {
            if (_onClock.Add(key)) _clock.Enqueue(key);
            var turn = _clock.Count;
            for (var step = 0;
                 step < 2 * turn && (_entries.Count > _capacity || _clock.Count > 2 * _capacity) && _clock.TryDequeue(out var candidate);
                 step++)
            {
                if (!_entries.TryGetValue(candidate, out var entry))
                {
                    _onClock.Remove(candidate);
                    continue;
                }

                var spared = _entries.Count <= _capacity || EqualityComparer<TKey>.Default.Equals(candidate, key) ||
                             (step < turn && entry.Spare());
                if (spared || !_entries.TryRemove(new KeyValuePair<TKey, Entry>(candidate, entry)))
                {
                    _clock.Enqueue(candidate);
                    continue;
                }

                _onClock.Remove(candidate);
            }
        }
    }

    private Entry New(TKey key, long generation) => new(generation,
        new Lazy<Task<TValue>>(() => _compile(key, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));

    private sealed class Entry(long generation, Lazy<Task<TValue>> value)
    {
        private int _read;

        public long Generation { get; } = generation;
        public Lazy<Task<TValue>> Value { get; } = value;

        /// <summary>Marks the entry read; the hand spares it once.</summary>
        public void Touch()
        {
            if (Volatile.Read(ref _read) == 0) Volatile.Write(ref _read, 1);
        }

        /// <summary>Whether the entry was read since the hand last passed, clearing the mark.</summary>
        public bool Spare() => Interlocked.Exchange(ref _read, 0) == 1;
    }
}
