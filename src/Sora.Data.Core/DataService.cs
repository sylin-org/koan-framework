using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core.Configuration;
using Sora.Data.Abstractions.Annotations;

namespace Sora.Data.Core;

/// <summary>
/// Provides access to aggregate repositories resolved from configured adapters.
/// Acts as a thin service-layer entry point used by high-level extensions.
/// </summary>
public interface IDataService
{
    /// <summary>
    /// Get a repository for the specified aggregate and key type.
    /// Implementations may cache resolved repositories for performance.
    /// </summary>
    IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
    where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Escape-hatch entry for direct commands against a named source or adapter.
    /// Returns a session for running ad-hoc queries/commands with optional connection override.
    /// </summary>
    Sora.Data.Core.Direct.IDirectSession Direct(string sourceOrAdapter);
}

/// <summary>
/// Default <see cref="IDataService"/> implementation.
/// Uses <see cref="AggregateConfigs"/> to resolve and cache repositories per (entity,key) pair.
/// </summary>
public sealed class DataService(IServiceProvider sp) : IDataService
{
    private readonly ConcurrentDictionary<(Type, Type), object> _cache = new();

    /// <inheritdoc />
    public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (_cache.TryGetValue(key, out var existing)) return (IDataRepository<TEntity, TKey>)existing;

    var cfg = AggregateConfigs.Get<TEntity, TKey>(sp);
        var repo = cfg.Repository;
        _cache[key] = repo;
        return repo;
    }

    /// <inheritdoc />
    public Sora.Data.Core.Direct.IDirectSession Direct(string sourceOrAdapter)
    {
        var svc = sp.GetService<Sora.Data.Core.Direct.IDirectDataService>()
            ?? throw new InvalidOperationException("IDirectDataService not registered. AddSoraDataDirect() required.");
        return svc.Direct(sourceOrAdapter);
    }
    // Provider resolution is now handled by TypeConfigs
}
