namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// What kind of invalidation a <see cref="CacheInvalidation"/> message describes.
/// </summary>
public enum CacheInvalidationKind
{
    /// <summary>Evict a single key on receivers. <c>Key</c> is required; <c>Tags</c> is ignored.</summary>
    EvictKey,

    /// <summary>Evict all entries carrying any of <c>Tags</c>. <c>Key</c> is ignored.</summary>
    EvictByTag,

    /// <summary>Evict the entire local cache. Use sparingly — sledgehammer.</summary>
    EvictAll
}
