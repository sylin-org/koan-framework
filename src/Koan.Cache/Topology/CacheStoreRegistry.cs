using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Cache.Topology;

/// <summary>
/// Thread-safe registry of cache stores. Self-populates from DI-registered
/// <c>IEnumerable&lt;ICacheStore&gt;</c> at construction; supports later manual
/// <see cref="Register"/> for dynamic scenarios. <see cref="CacheTopologyResolver"/>
/// consumes the snapshot to pick L1 + L2 tiers.
/// </summary>
internal sealed class CacheStoreRegistry : ICacheStoreRegistry
{
    private readonly object _lock = new();
    private readonly List<ICacheStore> _stores = new();

    public CacheStoreRegistry(IEnumerable<ICacheStore>? stores = null)
    {
        if (stores is null) return;
        foreach (var store in stores)
            Register(store);
    }

    public IReadOnlyList<ICacheStore> Stores
    {
        get
        {
            lock (_lock) { return _stores.ToList(); }
        }
    }

    public void Register(ICacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        lock (_lock)
        {
            var existing = _stores.FirstOrDefault(
                candidate => candidate.Name.Equals(store.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (ReferenceEquals(existing, store))
                    return;

                throw new InvalidOperationException(
                    $"Cache store identity '{store.Name}' was registered more than once. " +
                    "Give each cache provider a unique identity or remove the duplicate registration.");
            }

            _stores.Add(store);
        }
    }

    public ICacheStore? FindByName(string name)
    {
        lock (_lock)
        {
            return _stores.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
