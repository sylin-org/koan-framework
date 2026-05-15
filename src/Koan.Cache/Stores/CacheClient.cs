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
using Koan.Cache.Coherence;
using Koan.Cache.Diagnostics;
using Koan.Cache.Options;
using Koan.Cache.Scope;
using Koan.Cache.Stores.Internal;
using Koan.Cache.Topology;
using Koan.Core.Singleflight;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Stores;

/// <summary>
/// Public typed surface over <see cref="LayeredCache"/>. Owns serializer resolution,
/// scope projection, singleflight gating, and developer-facing options shaping.
/// </summary>
internal sealed class CacheClient : ICacheClient
{
    private const string LayeredProviderTag = "layered";

    private readonly LayeredCache _layered;
    private readonly IReadOnlyList<ICacheSerializer> _serializers;
    private readonly ISingleflightRegistry _singleflight;
    private readonly ICacheScopeAccessor _scopeAccessor;
    private readonly CacheInstrumentation _instrumentation;
    private readonly CoherenceCoordinator _coherence;
    private readonly IOptionsMonitor<CacheOptions> _options;
    private readonly ILogger<CacheClient> _logger;
    private readonly ConcurrentDictionary<Type, ICacheSerializer> _serializerCache = new();

    public CacheClient(
        LayeredCache layered,
        IEnumerable<ICacheSerializer> serializers,
        ISingleflightRegistry singleflight,
        ICacheScopeAccessor scopeAccessor,
        CacheInstrumentation instrumentation,
        CoherenceCoordinator coherence,
        IOptionsMonitor<CacheOptions> options,
        ILogger<CacheClient> logger)
    {
        _layered = layered ?? throw new ArgumentNullException(nameof(layered));
        _serializers = serializers?.ToArray() ?? Array.Empty<ICacheSerializer>();
        if (_serializers.Count == 0)
            throw new InvalidOperationException("No cache serializers registered. Ensure AddKoanCache() was called.");

        _singleflight = singleflight ?? throw new ArgumentNullException(nameof(singleflight));
        _coherence = coherence ?? throw new ArgumentNullException(nameof(coherence));
        _scopeAccessor = scopeAccessor;
        _instrumentation = instrumentation;
        _options = options;
        _logger = logger;
    }

    public CacheScopeHandle BeginScope(string scopeId, string? region = null)
    {
        var context = _scopeAccessor.Push(scopeId, region ?? _options.CurrentValue.DefaultRegion);
        return new CacheScopeHandle(scopeId, region, () => _scopeAccessor.Pop(context));
    }

    public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key)
    {
        // Per ARCH-0078: default consistency is Strict. Callers opt into SWR via .AllowStaleFor(...)
        // on the builder, or via [Cacheable(AllowStaleForSeconds = N)] at the entity level.
        var options = new CacheEntryOptions
        {
            Consistency = CacheConsistencyMode.Strict,
            SingleflightTimeout = _options.CurrentValue.DefaultSingleflightTimeout
        };

        var scope = _scopeAccessor.Current;
        if (scope.HasScope)
            options = options with { ScopeId = scope.ScopeId, Region = scope.Region ?? _options.CurrentValue.DefaultRegion };

        return new CacheEntryBuilder<T>(this, key, options);
    }

    public async ValueTask<CacheFetchResult> Get(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var normalized = ApplyScope(options);
        var result = await _layered.Read(key, normalized.ToReadOptions(), ct).ConfigureAwait(false);
        if (result.Hit) _instrumentation.RecordHit(key.Value, LayeredProviderTag);
        else _instrumentation.RecordMiss(key.Value, LayeredProviderTag);
        return result;
    }

    public async ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var (found, value) = await TryGetValueAsync<T>(key, options, ct).ConfigureAwait(false);
        return found ? value : default;
    }

    public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct)
    {
        if (valueFactory is null) throw new ArgumentNullException(nameof(valueFactory));
        return ExecuteGetOrAddAsync(key, valueFactory, options, ct);
    }

    public ValueTask<bool> Exists(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        _ = ApplyScope(options);
        return _layered.Exists(key, ct);
    }

    public async ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct)
    {
        if (value is null)
        {
            await Remove(key, ct).ConfigureAwait(false);
            return;
        }

        var normalized = ApplyScope(options);
        var serializer = ResolveSerializer(typeof(T), normalized.ContentKind);
        var cacheValue = await serializer.Serialize(value!, typeof(T), normalized, ct).ConfigureAwait(false);
        await _layered.Write(key, cacheValue, normalized.ToWriteOptions(), ct).ConfigureAwait(false);
        _instrumentation.RecordSet(key.Value, LayeredProviderTag);

        if (normalized.ForceCoherenceBroadcast)
        {
            await _coherence.BroadcastEvict(key, normalized.Region, ct).ConfigureAwait(false);
            _instrumentation.RecordInvalidation(key.Value, LayeredProviderTag, "write-broadcast");
        }
    }

    public async ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        var success = await _layered.Evict(key, ct).ConfigureAwait(false);
        _instrumentation.RecordRemove(key.Value, LayeredProviderTag, success);
        // Removes always broadcast — peers must drop their L1 entries even if the key wasn't present locally.
        await _coherence.BroadcastEvict(key, region: null, ct).ConfigureAwait(false);
        return success;
    }

    public ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var normalized = ApplyScope(options);
        return _layered.Touch(key, normalized.AbsoluteTtl, ct);
    }

    public async ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        if (tags is null) throw new ArgumentNullException(nameof(tags));

        var normalized = NormalizeTags(tags);
        if (normalized.Count == 0) return 0;

        ct.ThrowIfCancellationRequested();

        var keys = new HashSet<CacheKey>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tag in normalized)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var entry in _layered.EnumerateByTag(tag, ct).ConfigureAwait(false))
            {
                if (entry.IsExpired(now))
                {
                    await Remove(entry.Key, ct).ConfigureAwait(false);
                    continue;
                }

                keys.Add(entry.Key);
            }
        }

        var removed = 0L;
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();

            if (await Remove(key, ct).ConfigureAwait(false))
                removed++;
        }

        return removed;
    }

    public async ValueTask<long> CountTags(IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        if (tags is null) throw new ArgumentNullException(nameof(tags));

        var normalized = NormalizeTags(tags);
        if (normalized.Count == 0) return 0;

        ct.ThrowIfCancellationRequested();

        var keys = new HashSet<CacheKey>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tag in normalized)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var entry in _layered.EnumerateByTag(tag, ct).ConfigureAwait(false))
            {
                if (entry.IsExpired(now))
                {
                    await Remove(entry.Key, ct).ConfigureAwait(false);
                    continue;
                }

                keys.Add(entry.Key);
            }
        }

        return keys.Count;
    }

    internal async ValueTask<(bool Found, T? Value)> TryGetValueAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        var result = await Get(key, options, ct).ConfigureAwait(false);
        if (!result.Hit || result.Value is null)
            return (false, default);

        var serializer = ResolveSerializer(typeof(T), result.Options.ContentKind);
        var value = await serializer.DeserializeAsync<T>(result.Value, ct).ConfigureAwait(false);
        return (true, value);
    }

    private CacheEntryOptions ApplyScope(CacheEntryOptions options)
    {
        var scope = _scopeAccessor.Current;
        if (!scope.HasScope)
        {
            if (string.IsNullOrWhiteSpace(options.Region))
                return options with { Region = _options.CurrentValue.DefaultRegion };
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
                candidate = _serializers.FirstOrDefault(s => string.Equals(s.ContentType, CacheConstants.ContentTypes.String, StringComparison.OrdinalIgnoreCase));
            else if (kind == CacheContentKind.Binary)
                candidate = _serializers.FirstOrDefault(s => string.Equals(s.ContentType, CacheConstants.ContentTypes.Binary, StringComparison.OrdinalIgnoreCase));

            candidate ??= _serializers.FirstOrDefault(s => s.CanHandle(t));
            candidate ??= _serializers.First();
            return candidate;
        });
    }

    private async ValueTask<T?> ExecuteGetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct)
    {
        var (found, existing) = await TryGetValueAsync<T>(key, options, ct).ConfigureAwait(false);
        if (found) return existing;

        var timeout = options.SingleflightTimeout ?? _options.CurrentValue.DefaultSingleflightTimeout;
        return await _singleflight.RunAsync<T?>(key.Value, timeout, async innerCt =>
        {
            var (innerFound, innerValue) = await TryGetValueAsync<T>(key, options, innerCt).ConfigureAwait(false);
            if (innerFound) return innerValue;

            var created = await valueFactory(innerCt).ConfigureAwait(false);
            if (created is null) return default;

            await SetAsync(key, created, options, innerCt).ConfigureAwait(false);
            return created;
        }, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            set.Add(tag.Trim());
        }
        return set.Count == 0 ? Array.Empty<string>() : set.ToArray();
    }
}
