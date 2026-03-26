using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Cache.Stores;

/// <summary>
/// Thread-safe registry that collects individual cache store providers.
/// Populated during DI setup and consumed by <see cref="LayeredCacheStore"/>.
/// </summary>
internal sealed class CacheStoreRegistry : ICacheStoreRegistry
{
    private readonly object _lock = new();
    private readonly List<ICacheStore> _stores = [];

    public IReadOnlyList<ICacheStore> Stores
    {
        get
        {
            lock (_lock)
            {
                return _stores.ToList();
            }
        }
    }

    public void Register(ICacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        lock (_lock)
        {
            if (_stores.Any(s => s.ProviderName.Equals(store.ProviderName, StringComparison.OrdinalIgnoreCase)))
            {
                return; // idempotent
            }

            _stores.Add(store);
        }
    }

    public ICacheStore? FindByName(string providerName)
    {
        lock (_lock)
        {
            return _stores.FirstOrDefault(s =>
                s.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
