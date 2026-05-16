namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Declares whether a cache store is process-local or shared across nodes.
/// The topology resolver uses this to assign stores to the L1 and L2 tiers.
/// </summary>
public enum CacheStorePlacement
{
    /// <summary>Process-local cache (Memory, SQLite-on-disk). L1 candidate.</summary>
    Local,

    /// <summary>Shared across nodes (Redis, Memcached, ...). L2 candidate.</summary>
    Remote
}
