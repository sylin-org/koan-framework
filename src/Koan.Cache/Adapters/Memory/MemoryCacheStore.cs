using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Capabilities;
using Koan.Cache.Abstractions.Stores;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Core.Capabilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Adapters.Memory;

/// <summary>
/// Process-local <see cref="ICacheStore"/> backed by <see cref="IMemoryCache"/>.
/// Default L1 candidate; lower <c>[ProviderPriority]</c> than persistent stores like SQLite.
/// </summary>
[ProviderPriority(10)]
internal sealed class MemoryCacheStore : ICacheStore
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheAdapterOptions _options;
    private readonly ILogger<MemoryCacheStore> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new(StringComparer.OrdinalIgnoreCase);

    public MemoryCacheStore(IMemoryCache cache, IOptions<MemoryCacheAdapterOptions> options, ILogger<MemoryCacheStore> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "memory";

    public CacheStorePlacement Placement => CacheStorePlacement.Local;

    public void Describe(ICapabilities caps)
        => caps.Add(CacheCaps.Tags)
            .Add(CacheCaps.SlidingExpiration)
            .Add(CacheCaps.BoundedStaleServing)
            .Add(CacheCaps.BinaryPayload);

    public ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cache.TryGetValue(key.Value, out CacheEnvelope? envelope) || envelope is null)
            return ValueTask.FromResult(CacheFetchResult.Miss(new CacheEntryOptions()));

        var now = DateTimeOffset.UtcNow;
        // staleUntil is the storage-level eviction ceiling — past this, the entry is gone for everyone.
        if (envelope.StaleUntil is { } finalExpiry && finalExpiry <= now)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(CacheFetchResult.Miss(new CacheEntryOptions()));
        }

        // Per ARCH-0078: read-side AllowStaleFor is the master signal. Past the absolute TTL, the
        // caller must have explicitly opted into staleness for this read or the store treats it as Miss.
        var absoluteExpired = envelope.AbsoluteExpiration is { } abs && abs <= now;
        if (absoluteExpired && options.AllowStaleFor is null)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(CacheFetchResult.Miss(new CacheEntryOptions()));
        }

        if (absoluteExpired && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Serving stale memory cache entry for {CacheKey} (caller opted-in via AllowStaleFor)", key.Value);

        return ValueTask.FromResult(CacheFetchResult.HitResult(
            envelope.Value,
            envelope.Options,
            envelope.AbsoluteExpiration,
            envelope.StaleUntil));
    }

    public ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = options.AbsoluteTtl.HasValue ? now.Add(options.AbsoluteTtl.Value) : (DateTimeOffset?)null;
        var staleUntil = absoluteExpiration;
        if (options.AllowStaleFor.HasValue && absoluteExpiration.HasValue)
            staleUntil = absoluteExpiration.Value.Add(options.AllowStaleFor.Value);

        if (_cache.TryGetValue(key.Value, out CacheEnvelope? existing) && existing is not null)
            RemoveTags(key.Value, existing.Tags);

        var tagArray = options.Tags.Count == 0
            ? Array.Empty<string>()
            : options.Tags
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var storedOptions = CacheEntryOptions.FromWriteOptions(options);
        var envelope = new CacheEnvelope(value, storedOptions, now, absoluteExpiration, staleUntil, tagArray);

        // IMemoryCache must hold the entry until the staleness ceiling expires; otherwise opted-in
        // readers can't see stale values even when the contract allows them. Per ARCH-0078, the
        // staleUntil is set at write time iff the writer specified AllowStaleFor.
        var entryOptions = new MemoryCacheEntryOptions();
        if (staleUntil.HasValue)
            entryOptions.AbsoluteExpiration = staleUntil;
        else if (absoluteExpiration.HasValue)
            entryOptions.AbsoluteExpiration = absoluteExpiration;

        if (options.SlidingTtl.HasValue)
            entryOptions.SlidingExpiration = options.SlidingTtl;

        entryOptions.Priority = CacheItemPriority.Normal;
        entryOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = (_, state, _, _) =>
            {
                if (state is CacheEnvelope env) RemoveTags(key.Value, env.Tags);
                else RemoveTags(key.Value, Array.Empty<string>());
            },
            State = envelope
        });

        _cache.Set(key.Value, envelope, entryOptions);
        IndexTags(key.Value, envelope.Tags);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var existed = _cache.TryGetValue(key.Value, out CacheEnvelope? envelope);
        _cache.Remove(key.Value);
        if (existed && envelope is not null)
            RemoveTags(key.Value, envelope.Tags);

        return ValueTask.FromResult(existed);
    }

    public ValueTask Touch(CacheKey key, TimeSpan? newAbsoluteTtl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cache.TryGetValue(key.Value, out CacheEnvelope? envelope) || envelope is null)
            return ValueTask.CompletedTask;

        var refreshedOptions = envelope.Options with { AbsoluteTtl = newAbsoluteTtl };
        var writeOpts = refreshedOptions.ToWriteOptions();
        return Set(key, envelope.Value, writeOpts, ct);
    }

    public ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cache.TryGetValue(key.Value, out CacheEnvelope? envelope) || envelope is null)
            return ValueTask.FromResult(false);

        var now = DateTimeOffset.UtcNow;
        // Per ARCH-0078: Exists reports storage presence (entry within staleness ceiling).
        // Whether a specific Fetch surfaces a stale value is the reader's per-call opt-in, not
        // a storage-level decision.
        if (envelope.StaleUntil is { } staleUntil && staleUntil <= now)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_tagIndex.TryGetValue(tag, out var keys))
            yield break;

        await Task.Yield();

        foreach (var kvp in keys.Keys)
        {
            ct.ThrowIfCancellationRequested();
            if (_cache.TryGetValue(kvp, out CacheEnvelope? envelope) && envelope is not null)
                yield return new TaggedCacheKey(tag, new CacheKey(kvp), envelope.AbsoluteExpiration);
        }
    }

    private void IndexTags(string key, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) return;

        foreach (var tag in tags)
        {
            if (!_tagIndex.ContainsKey(tag) && _tagIndex.Count >= _options.TagIndexCapacity)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Skipping tag index registration for {Tag} due to capacity.", tag);

                continue;
            }

            var index = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            index[key] = 0;
        }
    }

    private void RemoveTags(string key, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) return;

        foreach (var tag in tags)
        {
            if (_tagIndex.TryGetValue(tag, out var index))
            {
                index.TryRemove(key, out _);
                if (index.IsEmpty)
                    _tagIndex.TryRemove(tag, out _);
            }
        }
    }

    private sealed record CacheEnvelope(
        CacheValue Value,
        CacheEntryOptions Options,
        DateTimeOffset Created,
        DateTimeOffset? AbsoluteExpiration,
        DateTimeOffset? StaleUntil,
        IReadOnlyList<string> Tags);
}
