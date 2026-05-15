using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Tests.Cache.Topology.Support;

/// <summary>
/// Test double — a minimal in-memory <see cref="ICacheStore"/> with controllable placement.
/// Records every operation for assertion. Tag enumeration is supported.
/// </summary>
internal class FakeCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new(StringComparer.OrdinalIgnoreCase);

    public FakeCacheStore(string name, CacheStorePlacement placement, CacheStoreCapabilities? capabilities = null)
    {
        Name = name;
        Placement = placement;
        Capabilities = capabilities ?? new CacheStoreCapabilities(
            SupportsTags: true,
            SupportsSlidingTtl: false,
            SupportsStaleWhileRevalidate: false,
            SupportsBinary: true,
            SupportsPersistence: false);
    }

    public string Name { get; }
    public CacheStorePlacement Placement { get; }
    public CacheStoreCapabilities Capabilities { get; }

    public int FetchCount;
    public int SetCount;
    public int RemoveCount;

    public ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct)
    {
        Interlocked.Increment(ref FetchCount);
        if (_entries.TryGetValue(key.Value, out var entry))
        {
            return ValueTask.FromResult(CacheFetchResult.HitResult(
                entry.Value, entry.Options, entry.AbsoluteExpiration, entry.StaleUntil));
        }

        return ValueTask.FromResult(CacheFetchResult.Miss(new CacheEntryOptions()));
    }

    public ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct)
    {
        Interlocked.Increment(ref SetCount);
        var entryOptions = CacheEntryOptions.FromWriteOptions(options);
        var now = DateTimeOffset.UtcNow;
        var absolute = options.AbsoluteTtl.HasValue ? now.Add(options.AbsoluteTtl.Value) : (DateTimeOffset?)null;
        _entries[key.Value] = new Entry(value, entryOptions, absolute, absolute, options.Tags);

        foreach (var tag in options.Tags)
        {
            var bucket = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            bucket[key.Value] = 0;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        Interlocked.Increment(ref RemoveCount);
        if (_entries.TryRemove(key.Value, out var entry))
        {
            foreach (var tag in entry.Tags)
            {
                if (_tagIndex.TryGetValue(tag, out var bucket))
                    bucket.TryRemove(key.Value, out _);
            }
            return ValueTask.FromResult(true);
        }
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
        => ValueTask.FromResult(_entries.ContainsKey(key.Value));

    public ValueTask Touch(CacheKey key, TimeSpan? newAbsoluteTtl, CancellationToken ct)
    {
        if (_entries.TryGetValue(key.Value, out var entry) && newAbsoluteTtl.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            _entries[key.Value] = entry with { AbsoluteExpiration = now.Add(newAbsoluteTtl.Value) };
        }
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_tagIndex.TryGetValue(tag, out var bucket)) yield break;
        await Task.Yield();

        foreach (var keyName in bucket.Keys)
        {
            if (_entries.TryGetValue(keyName, out var entry))
                yield return new TaggedCacheKey(tag, new CacheKey(keyName), entry.AbsoluteExpiration);
        }
    }

    public bool Contains(string key) => _entries.ContainsKey(key);

    private sealed record Entry(
        CacheValue Value,
        CacheEntryOptions Options,
        DateTimeOffset? AbsoluteExpiration,
        DateTimeOffset? StaleUntil,
        IReadOnlySet<string> Tags);
}
