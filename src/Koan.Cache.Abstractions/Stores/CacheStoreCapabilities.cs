namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Declares what storage features a cache store supports. Pure K/V capabilities only —
/// distributed-coherence capabilities live on <c>CoherenceCapabilities</c> in the
/// <c>Koan.Cache.Abstractions.Coherence</c> namespace.
/// </summary>
public sealed record CacheStoreCapabilities(
    bool SupportsTags,
    bool SupportsSlidingTtl,
    bool SupportsStaleWhileRevalidate,
    bool SupportsBinary,
    bool SupportsPersistence)
{
    /// <summary>No capabilities declared. Useful for unconfigured/null stores.</summary>
    public static CacheStoreCapabilities None { get; } = new(false, false, false, false, false);
}
