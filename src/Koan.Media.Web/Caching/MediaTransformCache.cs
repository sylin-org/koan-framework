using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Media.Web.Caching;

/// <summary>
/// Default <see cref="IMediaTransformCache"/> backed by the <c>Koan.Cache</c> pillar
/// (v0.7.0 migration). Rides whatever cache topology is registered — Memory only by default,
/// SQLite for persistence across restart, or Redis for cross-node sharing of transforms.
/// </summary>
/// <remarks>
/// <para>
/// <b>What changed in v0.7.0:</b> this implementation replaces the prior
/// <c>InMemoryMediaTransformCache</c>'s direct <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// usage with the cache pillar. Cross-node sharing comes for free when an L2 adapter is
/// referenced; byte-accurate LRU eviction is replaced with TTL eviction (see
/// <see cref="MediaTransformCacheOptions"/> for the migration trade-off).
/// </para>
/// <para>
/// <b>Best-effort writes:</b> any cache failure during <see cref="WriteAsync"/> is swallowed so
/// a flaky cache layer can never break a successful transform response — the controller already
/// returns bytes to the client before <see cref="WriteAsync"/> runs.
/// </para>
/// </remarks>
internal sealed class MediaTransformCache : IMediaTransformCache
{
    private readonly ICacheClient _cache;
    private readonly MediaTransformCacheOptions _options;
    private readonly ILogger<MediaTransformCache> _logger;

    public MediaTransformCache(
        ICacheClient cache,
        IOptions<MediaTransformCacheOptions> options,
        ILogger<MediaTransformCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaCacheEntry?> TryGetAsync(string cacheKey, CancellationToken ct)
    {
        try
        {
            var key = BuildKey(cacheKey);
            return await _cache.CreateEntry<MediaCacheEntry>(key)
                .WithContentKind(CacheContentKind.Json)
                .Get(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Koan.Media.Web: transform cache read failed for {Key}; falling through to recompute.", cacheKey);
            return null;
        }
    }

    public async Task WriteAsync(string cacheKey, MediaCacheEntry entry, CancellationToken ct)
    {
        if (entry is null) return;

        // Per-entry cap: huge transforms (4k JPEG, etc.) write through without caching so
        // they don't dominate memory. Next identical request re-encodes — slow but bounded.
        if (entry.Bytes.LongLength > _options.MaxEntryBytes)
        {
            _logger.LogDebug("Koan.Media.Web: skipping cache write for {Key} ({Bytes} bytes > {Cap} cap).",
                cacheKey, entry.Bytes.LongLength, _options.MaxEntryBytes);
            return;
        }

        try
        {
            var key = BuildKey(cacheKey);
            var builder = _cache.CreateEntry<MediaCacheEntry>(key)
                .WithContentKind(CacheContentKind.Json)
                .WithTags(_options.CacheTag);

            if (_options.AbsoluteExpiration is { } absolute && absolute > TimeSpan.Zero)
                builder = builder.WithAbsoluteTtl(absolute);

            if (_options.SlidingExpiration is { } sliding && sliding > TimeSpan.Zero)
                builder = builder.WithSlidingTtl(sliding);

            // Transform cache reads don't broadcast (they're populates, not data changes).
            // Writes here ARE data populates too (transform output, not entity state), so
            // suppress the coherence broadcast — peer nodes will compute their own on demand
            // or read from shared L2 if Redis is in play.
            builder = builder.BroadcastInvalidation(false);

            await builder.Set(entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort by contract — the controller swallows our exceptions anyway, but
            // we log so flaky-cache symptoms surface.
            _logger.LogDebug(ex, "Koan.Media.Web: transform cache write failed for {Key}; response already served.", cacheKey);
        }
    }

    private CacheKey BuildKey(string cacheKey)
    {
        var prefix = string.IsNullOrEmpty(_options.KeyPrefix) ? "" : _options.KeyPrefix;
        return new CacheKey(prefix + cacheKey);
    }
}
