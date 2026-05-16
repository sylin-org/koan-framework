using System;

namespace Koan.Media.Web.Caching;

/// <summary>
/// Configuration for the transform cache. Pass through
/// <c>services.AddMediaTransformCache(opts =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>v0.7.0 migration note</b>: the transform cache now rides the <c>Koan.Cache</c> pillar
/// instead of a dedicated <c>IMemoryCache</c>. This trades byte-accurate LRU eviction for
/// time-based eviction plus the pillar's cross-node coherence story. Concretely:
/// </para>
/// <list type="bullet">
///   <item><b>SizeLimitBytes is no longer honored</b> — kept as a deprecated property so
///   existing config bindings don't break, but the cache pillar evicts by TTL, not by total
///   byte budget. Bound memory via <see cref="AbsoluteExpiration"/> instead.</item>
///   <item><b>AbsoluteExpiration now defaults to 6 hours</b> (was <c>null</c>). Without an
///   expiration AND without byte-size eviction, the cache would grow unbounded — the default
///   gives a sane upper bound that ops can tune.</item>
///   <item><b>MaxEntryBytes still applies</b> — too-large transforms are skipped at write
///   time (no caching) to keep memory bounded for the common case.</item>
///   <item><b>Multi-node deployments</b> with <c>Koan.Cache.Adapter.Redis</c> referenced
///   get cross-node sharing automatically — a transform computed on node A becomes
///   available to node B without recompute.</item>
/// </list>
/// </remarks>
public sealed class MediaTransformCacheOptions
{
    /// <summary>
    /// DEPRECATED in v0.7.0. The pillar-backed cache does not enforce a total byte budget;
    /// use <see cref="AbsoluteExpiration"/> to bound memory through TTL eviction. This
    /// property is preserved for binding compatibility but has no runtime effect.
    /// </summary>
    [Obsolete("Pillar-backed cache evicts by TTL, not byte budget. Use AbsoluteExpiration to bound memory. Will be removed in a future release.")]
    public long SizeLimitBytes { get; set; } = 128L * 1024L * 1024L;

    /// <summary>
    /// Per-entry inactivity window before eviction. <c>null</c> by default — the absolute
    /// TTL is the primary bound. Set if you want LRU-style aging on top of the absolute TTL.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Absolute max age before eviction regardless of access. Default 6 hours, raised from
    /// <c>null</c> in v0.7.0 — see migration note in the class doc.
    /// </summary>
    public TimeSpan? AbsoluteExpiration { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Soft per-entry size cap. Entries larger than this are NOT written to the cache —
    /// they're re-encoded on every request, slow but bounded. Default 4 MiB.
    /// </summary>
    public long MaxEntryBytes { get; set; } = 4L * 1024L * 1024L;

    /// <summary>
    /// Cache tag applied to all transform entries. Lets ops bulk-flush all transforms via
    /// <c>await Cache.Tags("media-transform").Flush(ct)</c>. Default <c>"media-transform"</c>.
    /// </summary>
    public string CacheTag { get; set; } = "media-transform";

    /// <summary>
    /// Cache key prefix. Transform keys are stored as <c>{KeyPrefix}{cacheKey}</c> in the
    /// cache pillar's keyspace. Default <c>"media-transform:"</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "media-transform:";
}
