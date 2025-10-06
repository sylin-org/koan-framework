using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Memory.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Adapter.Memory.Stores;

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

    public string ProviderName => "memory";

    public CacheCapabilities Capabilities { get; } = new(
        SupportsBinary: true,
        SupportsPubSubInvalidation: false,
        SupportsCompareExchange: false,
        SupportsRegionScoping: false,
        Hints: new HashSet<string>(new[] { "tags", "stale-while-revalidate", "singleflight" }, StringComparer.OrdinalIgnoreCase));

    public ValueTask<CacheFetchResult> FetchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cache.TryGetValue(key.Value, out CacheEnvelope? envelope) || envelope is null)
        {
            return ValueTask.FromResult(CacheFetchResult.Miss(options));
        }

        var now = DateTimeOffset.UtcNow;
        if (envelope.StaleUntil is { } finalExpiry && finalExpiry <= now)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(CacheFetchResult.Miss(options));
        }

        var absoluteExpired = envelope.AbsoluteExpiration is { } abs && abs <= now;
        if (absoluteExpired && !_options.EnableStaleWhileRevalidate)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(CacheFetchResult.Miss(options));
        }

        if (absoluteExpired && _options.EnableStaleWhileRevalidate && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Serving stale memory cache entry for {CacheKey}", key.Value);
        }

        return ValueTask.FromResult(CacheFetchResult.HitResult(
            envelope.Value,
            envelope.Options,
            envelope.AbsoluteExpiration,
            envelope.StaleUntil));
    }

    public ValueTask SetAsync(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = options.AbsoluteTtl.HasValue ? now.Add(options.AbsoluteTtl.Value) : (DateTimeOffset?)null;
        var staleUntil = absoluteExpiration;
        if (options.AllowStaleFor.HasValue && absoluteExpiration.HasValue)
        {
            staleUntil = absoluteExpiration.Value.Add(options.AllowStaleFor.Value);
        }

        if (_cache.TryGetValue(key.Value, out CacheEnvelope? existing) && existing is not null)
        {
            RemoveTags(key.Value, existing.Tags);
        }

        var tagArray = options.Tags.Count == 0
            ? Array.Empty<string>()
            : options.Tags.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var envelope = new CacheEnvelope(value, options, now, absoluteExpiration, staleUntil, tagArray);

        var entryOptions = new MemoryCacheEntryOptions();
        if (_options.EnableStaleWhileRevalidate && staleUntil.HasValue)
        {
            entryOptions.AbsoluteExpiration = staleUntil;
        }
        else if (absoluteExpiration.HasValue)
        {
            entryOptions.AbsoluteExpiration = absoluteExpiration;
        }

        if (options.SlidingTtl.HasValue)
        {
            entryOptions.SlidingExpiration = options.SlidingTtl;
        }

        entryOptions.Priority = CacheItemPriority.Normal;
        entryOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = (_, state, _, _) =>
            {
                if (state is CacheEnvelope env)
                {
                    RemoveTags(key.Value, env.Tags);
                }
                else
                {
                    RemoveTags(key.Value, Array.Empty<string>());
                }
            },
            State = envelope
        });

        _cache.Set(key.Value, envelope, entryOptions);
        IndexTags(key.Value, envelope.Tags);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var existed = _cache.TryGetValue(key.Value, out CacheEnvelope? envelope);
        _cache.Remove(key.Value);
        if (existed && envelope is not null)
        {
            RemoveTags(key.Value, envelope.Tags);
        }

        return ValueTask.FromResult(existed);
    }

    public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(key.Value, out CacheEnvelope? envelope) && envelope is not null)
        {
            return SetAsync(key, envelope.Value, envelope.Options, ct);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ExistsAsync(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cache.TryGetValue(key.Value, out CacheEnvelope? envelope) || envelope is null)
        {
            return ValueTask.FromResult(false);
        }

        var now = DateTimeOffset.UtcNow;
        if (envelope.StaleUntil is { } staleUntil && staleUntil <= now)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(false);
        }

        if (envelope.AbsoluteExpiration is { } absolute && absolute <= now && !_options.EnableStaleWhileRevalidate)
        {
            _cache.Remove(key.Value);
            RemoveTags(key.Value, envelope.Tags);
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask PublishInvalidationAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        // No-op for in-memory implementation; nothing to publish.
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTagAsync(string tag, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!_tagIndex.TryGetValue(tag, out var keys))
        {
            yield break;
        }

        await Task.Yield();

        foreach (var kvp in keys.Keys)
        {
            ct.ThrowIfCancellationRequested();
            if (_cache.TryGetValue(kvp, out CacheEnvelope? envelope) && envelope is not null)
            {
                yield return new TaggedCacheKey(tag, new CacheKey(kvp), envelope.AbsoluteExpiration);
            }
        }
    }

    private void IndexTags(string key, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return;
        }

        foreach (var tag in tags)
        {
            if (!_tagIndex.ContainsKey(tag) && _tagIndex.Count >= _options.TagIndexCapacity)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Skipping tag index registration for {Tag} due to capacity.", tag);
                }

                continue;
            }

            var index = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            index[key] = 0;
        }
    }

    private void RemoveTags(string key, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return;
        }

        foreach (var tag in tags)
        {
            if (_tagIndex.TryGetValue(tag, out var index))
            {
                index.TryRemove(key, out _);
                if (index.IsEmpty)
                {
                    _tagIndex.TryRemove(tag, out _);
                }
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
