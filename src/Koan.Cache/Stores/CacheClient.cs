using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Diagnostics;
using Koan.Cache.Options;
using Koan.Cache.Scope;
using Koan.Cache.Singleflight;
using Koan.Cache.Stores.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Stores;

internal sealed class CacheClient : ICacheClient
{
    private readonly ICacheStore _store;
    private readonly IReadOnlyList<ICacheSerializer> _serializers;
    private readonly CacheSingleflightRegistry _singleflight;
    private readonly ICacheScopeAccessor _scopeAccessor;
    private readonly CacheInstrumentation _instrumentation;
    private readonly IOptionsMonitor<CacheOptions> _options;
    private readonly ILogger<CacheClient> _logger;
    private readonly ConcurrentDictionary<Type, ICacheSerializer> _serializerCache = new();

    public CacheClient(
        ICacheStore store,
        IEnumerable<ICacheSerializer> serializers,
        CacheSingleflightRegistry singleflight,
        ICacheScopeAccessor scopeAccessor,
        CacheInstrumentation instrumentation,
        IOptionsMonitor<CacheOptions> options,
        ILogger<CacheClient> logger)
    {
        _store = store ?? throw new InvalidOperationException("ICacheStore is required. Ensure AddKoanCacheAdapter has been called.");
        _serializers = serializers?.ToArray() ?? Array.Empty<ICacheSerializer>();
        if (_serializers.Count == 0)
        {
            throw new InvalidOperationException("No cache serializers registered. Ensure AddKoanCache() was called.");
        }

        _singleflight = singleflight;
        _scopeAccessor = scopeAccessor;
        _instrumentation = instrumentation;
        _options = options;
        _logger = logger;
    }

    public ICacheStore Store => _store;

    public CacheScopeHandle BeginScope(string scopeId, string? region = null)
    {
        var context = _scopeAccessor.Push(scopeId, region ?? _options.CurrentValue.DefaultRegion);
        return new CacheScopeHandle(scopeId, region, () => _scopeAccessor.Pop(context));
    }

    public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key)
    {
        var options = new CacheEntryOptions
        {
            Consistency = CacheConsistencyMode.StaleWhileRevalidate,
            SingleflightTimeout = _options.CurrentValue.DefaultSingleflightTimeout
        };

        var scope = _scopeAccessor.Current;
        if (scope.HasScope)
        {
            options = options with { ScopeId = scope.ScopeId, Region = scope.Region ?? _options.CurrentValue.DefaultRegion };
        }

        return new CacheEntryBuilder<T>(this, key, options);
    }

    public async ValueTask<CacheFetchResult> GetAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var normalized = ApplyScope(options);
        var result = await _store.FetchAsync(key, normalized, ct);
        if (result.Hit)
        {
            _instrumentation.RecordHit(key.Value, _store.ProviderName);
        }
        else
        {
            _instrumentation.RecordMiss(key.Value, _store.ProviderName);
        }

        return result;
    }

    public async ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var (found, value) = await TryGetValueAsync<T>(key, options, ct);
        return found ? value : default;
    }

    public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct)
    {
        if (valueFactory is null)
        {
            throw new ArgumentNullException(nameof(valueFactory));
        }

        return ExecuteGetOrAddAsync(key, valueFactory, options, ct);
    }

    public ValueTask<bool> ExistsAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        _ = ApplyScope(options);
        return _store.ExistsAsync(key, ct);
    }

    public async ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct)
    {
        if (value is null)
        {
            await RemoveAsync(key, ct);
            return;
        }

        var normalized = ApplyScope(options);
        var serializer = ResolveSerializer(typeof(T), normalized.ContentKind);
        var cacheValue = await serializer.SerializeAsync(value!, normalized, ct);
        await _store.SetAsync(key, cacheValue, normalized, ct);
        _instrumentation.RecordSet(key.Value, _store.ProviderName);

        if (normalized.ForcePublishInvalidation)
        {
            await _store.PublishInvalidationAsync(key, normalized, ct);
            _instrumentation.RecordInvalidation(key.Value, _store.ProviderName, "publish");
        }
    }

    public async ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct)
    {
        var success = await _store.RemoveAsync(key, ct);
        _instrumentation.RecordRemove(key.Value, _store.ProviderName, success);
        if (!success && _store.Capabilities.SupportsPubSubInvalidation)
        {
            await _store.PublishInvalidationAsync(key, new CacheEntryOptions(), ct);
            _instrumentation.RecordInvalidation(key.Value, _store.ProviderName, "missing");
        }

        return success;
    }

    public ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var normalized = ApplyScope(options);
        return _store.TouchAsync(key, normalized, ct);
    }

    public async ValueTask<long> FlushTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        if (tags is null)
        {
            throw new ArgumentNullException(nameof(tags));
        }

        var normalized = NormalizeTags(tags);
        if (normalized.Count == 0)
        {
            return 0;
        }

        ct.ThrowIfCancellationRequested();

        var keys = new HashSet<CacheKey>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tag in normalized)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var entry in _store.EnumerateByTagAsync(tag, ct))
            {
                if (entry.IsExpired(now))
                {
                    await RemoveAsync(entry.Key, ct);
                    continue;
                }

                keys.Add(entry.Key);
            }
        }

        var removed = 0L;
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();

            if (await RemoveAsync(key, ct))
            {
                removed++;
            }
        }

        return removed;
    }

    public async ValueTask<long> CountTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        if (tags is null)
        {
            throw new ArgumentNullException(nameof(tags));
        }

        var normalized = NormalizeTags(tags);
        if (normalized.Count == 0)
        {
            return 0;
        }

        ct.ThrowIfCancellationRequested();

        var keys = new HashSet<CacheKey>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tag in normalized)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var entry in _store.EnumerateByTagAsync(tag, ct))
            {
                if (entry.IsExpired(now))
                {
                    await RemoveAsync(entry.Key, ct);
                    continue;
                }

                keys.Add(entry.Key);
            }
        }

        return keys.Count;
    }

    internal async ValueTask<(bool Found, T? Value)> TryGetValueAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var result = await GetAsync(key, options, ct);
        if (!result.Hit || result.Value is null)
        {
            return (false, default);
        }

        var serializer = ResolveSerializer(typeof(T), result.Options.ContentKind);
        var value = await serializer.DeserializeAsync<T>(result.Value, ct);
        return (true, value);
    }

    private CacheEntryOptions ApplyScope(CacheEntryOptions options)
    {
        var scope = _scopeAccessor.Current;
        if (!scope.HasScope)
        {
            if (string.IsNullOrWhiteSpace(options.Region))
            {
                return options with { Region = _options.CurrentValue.DefaultRegion };
            }

        }
        else
        {
            return options with
            {
                ScopeId = options.ScopeId ?? scope.ScopeId,
                Region = options.Region ?? scope.Region ?? _options.CurrentValue.DefaultRegion
            };
        }

        return options;
    }

    private ICacheSerializer ResolveSerializer(Type type, CacheContentKind kind)
    {
        return _serializerCache.GetOrAdd(type, t =>
        {
            ICacheSerializer? candidate = null;
            if (kind == CacheContentKind.String)
            {
                candidate = _serializers.FirstOrDefault(s => string.Equals(s.ContentType, CacheConstants.ContentTypes.String, StringComparison.OrdinalIgnoreCase));
            }
            else if (kind == CacheContentKind.Binary)
            {
                candidate = _serializers.FirstOrDefault(s => string.Equals(s.ContentType, CacheConstants.ContentTypes.Binary, StringComparison.OrdinalIgnoreCase));
            }

            candidate ??= _serializers.FirstOrDefault(s => s.CanHandle(t));
            candidate ??= _serializers.First();
            return candidate;
        });
    }

    private async ValueTask<T?> ExecuteGetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct)
    {
        var (found, existing) = await TryGetValueAsync<T>(key, options, ct);
        if (found)
        {
            return existing;
        }

        var timeout = options.SingleflightTimeout ?? _options.CurrentValue.DefaultSingleflightTimeout;
        return await _singleflight.RunAsync(key.Value, timeout, async innerCt =>
        {
            var (innerFound, innerValue) = await TryGetValueAsync<T>(key, options, innerCt);
            if (innerFound)
            {
                return innerValue;
            }

            var created = await valueFactory(innerCt);
            if (created is null)
            {
                return default;
            }

            await SetAsync(key, created, options, innerCt);
            return created;
        }, ct);
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            set.Add(tag.Trim());
        }

        return set.Count == 0 ? Array.Empty<string>() : set.ToArray();
    }
}
