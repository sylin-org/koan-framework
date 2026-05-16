namespace Koan.Cache.Abstractions.Policies;

/// <summary>
/// Per-request cache behavior override pushed via <c>EntityContext.WithCacheBehavior</c>.
/// Affects reads and cache-populating only — writes always invalidate.
/// </summary>
public enum CacheBehavior
{
    /// <summary>Honor the policy's declared strategy. Default.</summary>
    Default,

    /// <summary>Skip cache reads, hit the DB; do not populate cache. Writes still invalidate.</summary>
    Bypass,

    /// <summary>Skip cache reads, hit the DB, repopulate cache from the fresh value.</summary>
    Refresh,

    /// <summary>Read cache if present, fall through to DB on miss, but do not populate cache.</summary>
    ReadOnly
}
