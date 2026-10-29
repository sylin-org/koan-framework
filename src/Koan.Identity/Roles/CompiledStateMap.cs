using System.Collections.Concurrent;

namespace Koan.Identity.Roles;

/// <summary>
/// A bounded, single-flight map of compiled state. Each invalidation replaces the key's entry with a newer
/// generation, so a compilation that began before the change never returns as current: its caller compiles again.
/// </summary>
internal sealed class CompiledStateMap<TKey, TValue>(int capacity, Func<TKey, CancellationToken, Task<TValue>> compile)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

    public async Task<TValue> Get(TKey key, CancellationToken ct)
    {
        while (true)
        {
            if (_entries.Count >= capacity && !_entries.ContainsKey(key)) _entries.Clear();
            var entry = _entries.GetOrAdd(key, k => New(k, 0));
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

    public void InvalidateAll() { foreach (var key in _entries.Keys) Invalidate(key); }

    private Entry New(TKey key, long generation) => new(generation,
        new Lazy<Task<TValue>>(() => compile(key, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));

    private sealed record Entry(long Generation, Lazy<Task<TValue>> Value);
}
