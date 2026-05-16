using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Registry of individual cache store providers. Used by the topology resolver to
/// discover L1 (Local) and L2 (Remote) tier candidates. Individual adapters
/// self-register here via their <c>KoanAutoRegistrar</c>; the layered cache
/// receives the resolved (Local, Remote) pair via <c>CacheTopologyResolver</c>.
/// </summary>
public interface ICacheStoreRegistry
{
    /// <summary>All registered cache stores, in registration order.</summary>
    IReadOnlyList<ICacheStore> Stores { get; }

    /// <summary>Register a cache store. Idempotent — duplicate names are silently ignored.</summary>
    void Register(ICacheStore store);

    /// <summary>Find a store by <see cref="ICacheStore.Name"/> (case-insensitive). Returns null if not found.</summary>
    ICacheStore? FindByName(string name);
}
