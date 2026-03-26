using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Stores;

/// <summary>
/// Orchestrates L1 (local) and L2 (remote) cache tiers.
/// Default behavior: always layered, degrades gracefully when only one tier is available.
///
/// Read path:  L1 hit -> return. L1 miss -> L2 hit -> write-back L1, return. Both miss -> caller loads.
/// Write path: Write L1 + L2. L2 publishes invalidation -> other instances clear L1.
/// </summary>
internal sealed class LayeredCacheStore : ICacheStore
{
    private readonly ICacheStore? _local;   // L1
    private readonly ICacheStore? _remote;  // L2
    private readonly ILogger<LayeredCacheStore> _logger;

    public LayeredCacheStore(
        ICacheStoreRegistry registry,
        IOptions<CacheOptions> options,
        ILogger<LayeredCacheStore> logger)
    {
        _logger = logger;
        var allStores = registry.Stores;

        _local = ResolveLocal(allStores, options.Value);
        _remote = ResolveRemote(allStores, options.Value);

        if (_local is not null && _remote is not null)
            _logger.LogInformation("Cache layered: L1={Local}, L2={Remote}", _local.ProviderName, _remote.ProviderName);
        else if (_local is not null)
            _logger.LogInformation("Cache single-tier: L1={Local} (no remote available)", _local.ProviderName);
        else if (_remote is not null)
            _logger.LogInformation("Cache single-tier: L2={Remote} (no local available)", _remote.ProviderName);
        else
            _logger.LogWarning("Cache has no backing stores — all operations will no-op");
    }

    public string ProviderName => _local is not null && _remote is not null
        ? "layered"
        : (_local ?? _remote)?.ProviderName ?? "none";

    public CacheCapabilities Capabilities
    {
        get
        {
            if (_local is null && _remote is null)
                return CacheCapabilities.None;

            if (_local is null)
                return _remote!.Capabilities;

            if (_remote is null)
                return _local.Capabilities;

            // Merge: union of hints, OR of boolean capabilities
            var mergedHints = new HashSet<string>(
                _local.Capabilities.Hints.Concat(_remote.Capabilities.Hints),
                StringComparer.OrdinalIgnoreCase);

            return new CacheCapabilities(
                SupportsBinary: _local.Capabilities.SupportsBinary || _remote.Capabilities.SupportsBinary,
                SupportsPubSubInvalidation: _local.Capabilities.SupportsPubSubInvalidation || _remote.Capabilities.SupportsPubSubInvalidation,
                SupportsCompareExchange: _local.Capabilities.SupportsCompareExchange || _remote.Capabilities.SupportsCompareExchange,
                SupportsRegionScoping: _local.Capabilities.SupportsRegionScoping || _remote.Capabilities.SupportsRegionScoping,
                Hints: mergedHints,
                SupportsSharedAccess: _local.Capabilities.SupportsSharedAccess || _remote.Capabilities.SupportsSharedAccess,
                SupportsPersistence: _local.Capabilities.SupportsPersistence || _remote.Capabilities.SupportsPersistence);
        }
    }

    public async ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        // L1 hit -> return immediately
        if (_local is not null)
        {
            var l1Result = await _local.Fetch(key, options, ct);
            if (l1Result.Hit)
                return l1Result;
        }

        // L1 miss -> try L2
        if (_remote is not null)
        {
            var l2Result = await _remote.Fetch(key, options, ct);
            if (l2Result.Hit)
            {
                // Write-back to L1
                if (_local is not null && l2Result.Value is not null)
                {
                    await _local.Set(key, l2Result.Value, l2Result.Options, ct);
                }

                return l2Result;
            }
        }

        // Both miss
        return CacheFetchResult.Miss(options);
    }

    public async ValueTask Set(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct)
    {
        if (_local is not null)
            await _local.Set(key, value, options, ct);

        if (_remote is not null)
            await _remote.Set(key, value, options, ct);
    }

    public async ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        var l1Removed = _local is not null && await _local.Remove(key, ct);
        var l2Removed = _remote is not null && await _remote.Remove(key, ct);

        return l1Removed || l2Removed;
    }

    public async ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        if (_local is not null)
            await _local.Touch(key, options, ct);

        if (_remote is not null)
            await _remote.Touch(key, options, ct);
    }

    public async ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
    {
        if (_local is not null && await _local.Exists(key, ct))
            return true;

        if (_remote is not null && await _remote.Exists(key, ct))
            return true;

        return false;
    }

    public async ValueTask PublishInvalidation(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        // Delegate to L2 which has pub/sub capability; L1 is local-only
        if (_remote is not null)
            await _remote.PublishInvalidation(key, options, ct);
        else if (_local is not null)
            await _local.PublishInvalidation(key, options, ct);
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(
        string tag,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // L2 is authoritative for tag enumeration (shared state); fallback to L1
        var source = _remote ?? _local;
        if (source is null)
            yield break;

        await foreach (var entry in source.EnumerateByTag(tag, ct))
        {
            yield return entry;
        }
    }

    private static ICacheStore? ResolveLocal(IReadOnlyList<ICacheStore> stores, CacheOptions options)
    {
        if (options.LocalProvider is not null)
        {
            return stores.FirstOrDefault(s =>
                s.ProviderName.Equals(options.LocalProvider, StringComparison.OrdinalIgnoreCase));
        }

        return stores.FirstOrDefault(s => s.Capabilities.IsLocalOnly);
    }

    private static ICacheStore? ResolveRemote(IReadOnlyList<ICacheStore> stores, CacheOptions options)
    {
        if (options.RemoteProvider is not null)
        {
            return stores.FirstOrDefault(s =>
                s.ProviderName.Equals(options.RemoteProvider, StringComparison.OrdinalIgnoreCase));
        }

        return stores.FirstOrDefault(s => s.Capabilities.IsRemoteCapable);
    }
}
