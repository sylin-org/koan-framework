using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Configuration;
using Koan.Data.Vector.Abstractions;
using System.Collections.Concurrent;

namespace Koan.Data.Core;

/// <summary>
/// Default <see cref="IDataService"/> implementation.
/// Uses <see cref="AggregateConfigs"/> to resolve and cache repositories per (entity,key) pair.
/// </summary>
public sealed class DataService(IServiceProvider sp) : IDataService
{
    private readonly ConcurrentDictionary<(Type, Type), object> _cache = new();
    private readonly ConcurrentDictionary<(Type, Type), object> _vecCache = new();

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
    public Direct.IDirectSession Direct(string sourceOrAdapter)
    {
        var svc = sp.GetService<Direct.IDirectDataService>()
            ?? throw new InvalidOperationException("IDirectDataService not registered. AddKoanDataDirect() required.");
        return svc.Direct(sourceOrAdapter);
    }

    public IVectorSearchRepository<TEntity, TKey>? TryGetVectorRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (_vecCache.TryGetValue(key, out var existing)) return (IVectorSearchRepository<TEntity, TKey>?)existing;

        // Resolve from adapter factories honoring role attributes and defaults.
        var vectorFactories = sp.GetServices<IVectorAdapterFactory>().ToList();
        if (vectorFactories.Count == 0) return null;

        // 1) Role attribute: [VectorAdapter("...")]
        string? desired = (Attribute.GetCustomAttribute(typeof(TEntity), typeof(VectorAdapterAttribute))
            as VectorAdapterAttribute)?.Provider;

        // 2) App default: Koan:Data:VectorDefaults:DefaultProvider
        if (string.IsNullOrWhiteSpace(desired))
        {
            // If vector module is referenced, resolve defaults from there. Optional.
            try
            {
                var optType = typeof(Microsoft.Extensions.Options.IOptions<>).MakeGenericType(Type.GetType("Koan.Data.Vector.VectorDefaultsOptions, Koan.Data.Vector")!);
                var opts = sp.GetService(optType);
                if (opts is not null)
                {
                    var valProp = optType.GetProperty("Value");
                    var val = valProp?.GetValue(opts);
                    var prop = val?.GetType().GetProperty("DefaultProvider");
                    desired = (string?)prop?.GetValue(val);
                }
            }
            catch { /* optional */ }

            // Fallback: read straight from IConfiguration if options aren't bound
            if (string.IsNullOrWhiteSpace(desired))
            {
                var cfg = sp.GetService<IConfiguration>();
                var viaCfg = cfg?["Koan:Data:VectorDefaults:DefaultProvider"];
                if (!string.IsNullOrWhiteSpace(viaCfg)) desired = viaCfg;
            }
        }

        // 3) Fallback: entity data provider (useful when provider names align, e.g., "weaviate")
        if (string.IsNullOrWhiteSpace(desired))
        {
            desired = AggregateConfigs.Get<TEntity, TKey>(sp).Provider;
        }

        IVectorSearchRepository<TEntity, TKey>? repo = null;
        IVectorAdapterFactory? factory = null;
        if (!string.IsNullOrWhiteSpace(desired))
        {
            factory = vectorFactories.FirstOrDefault(f => f.CanHandle(desired!));
        }
        factory ??= vectorFactories.FirstOrDefault();
        if (factory is not null)
            repo = factory.Create<TEntity, TKey>(sp);

        if (repo is not null)
            _vecCache[key] = repo;
        return repo;
    }
    // Provider resolution is now handled by TypeConfigs
}
