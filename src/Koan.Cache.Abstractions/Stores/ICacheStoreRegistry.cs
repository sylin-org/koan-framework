using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Registry of individual cache store providers. Used by the layered store
/// to discover L1 (local) and L2 (remote) tiers without circular DI references.
/// Individual adapters register themselves here; <see cref="ICacheStore"/> consumers
/// receive the layered wrapper that orchestrates reads and writes across tiers.
/// </summary>
public interface ICacheStoreRegistry
{
    /// <summary>All registered individual cache stores, in registration order.</summary>
    IReadOnlyList<ICacheStore> Stores { get; }

    /// <summary>Register an individual cache store provider.</summary>
    void Register(ICacheStore store);

    /// <summary>Find a store by provider name (case-insensitive). Returns null if not found.</summary>
    ICacheStore? FindByName(string providerName);
}
