using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Tests.Cache.Coherence.InMemory.Support;

/// <summary>
/// Minimal in-memory <see cref="ICacheStore"/> for coherence integration tests. Each node
/// has its own Local instance; nodes can share a single Remote instance to model a shared L2.
/// </summary>
internal sealed class FakeCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new(StringComparer.OrdinalIgnoreCase);

    public FakeCacheStore(string name, CacheStorePlacement placement)
    {
        Name = name;
        Placement = placement;
    }

    public string Name { get; }
    public CacheStorePlacement Placement { get; }
    public CacheStoreCapabilities Capabilities { get; } = new(
        SupportsTags: true,
        SupportsSlidingTtl: false,
        SupportsStaleWhileRevalidate: false,
        SupportsBinary: true,
        SupportsPersistence: false);

    public ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct)
    {
        if (_entries.TryGetValue(key.Value, out var entry))
            return ValueTask.FromResult(CacheFetchResult.HitResult(entry.Value, entry.Options, null, null));
        return ValueTask.FromResult(CacheFetchResult.Miss(new CacheEntryOptions()));
    }

    public ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct)
    {
        var entryOptions = CacheEntryOptions.FromWriteOptions(options);
        _entries[key.Value] = new Entry(value, entryOptions, options.Tags);
        foreach (var tag in options.Tags)
        {
            var bucket = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            bucket[key.Value] = 0;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        if (_entries.TryRemove(key.Value, out var entry))
        {
            foreach (var tag in entry.Tags)
                if (_tagIndex.TryGetValue(tag, out var bucket))
                    bucket.TryRemove(key.Value, out _);
            return ValueTask.FromResult(true);
        }
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
        => ValueTask.FromResult(_entries.ContainsKey(key.Value));

    public ValueTask Touch(CacheKey key, TimeSpan? newAbsoluteTtl, CancellationToken ct) => ValueTask.CompletedTask;

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_tagIndex.TryGetValue(tag, out var bucket)) yield break;
        await Task.Yield();
        foreach (var keyName in bucket.Keys)
            if (_entries.ContainsKey(keyName))
                yield return new TaggedCacheKey(tag, new CacheKey(keyName), null);
    }

    public bool Contains(string key) => _entries.ContainsKey(key);

    private sealed record Entry(CacheValue Value, CacheEntryOptions Options, IReadOnlySet<string> Tags);
}
