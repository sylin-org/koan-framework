using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Schema;
using Koan.Data.Vector.Abstractions;
using System.Collections.Concurrent;

namespace Koan.Data.Core;

/// <summary>
/// Default <see cref="IDataService"/> implementation.
/// Uses multi-dimensional caching per (entity, key, adapter, source) combination.
/// </summary>
public sealed class DataService(IServiceProvider sp) : IDataService
{
    private readonly ConcurrentDictionary<CacheKey, object> _cache = new();
    private readonly ConcurrentDictionary<(Type, Type), object> _vecCache = new();

    private record CacheKey(
        Type EntityType,
        Type KeyType,
        string Adapter,
        string Source);

    /// <inheritdoc />
    public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var (adapter, source) = AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry);

        var key = new CacheKey(typeof(TEntity), typeof(TKey), adapter, source);

        if (_cache.TryGetValue(key, out var existing))
            return (IDataRepository<TEntity, TKey>)existing;

        // Find factory for adapter
        var factories = sp.GetServices<IDataAdapterFactory>();
        var factory = factories.FirstOrDefault(f => f.CanHandle(adapter))
            ?? throw new InvalidOperationException($"No data adapter factory for provider '{adapter}'");

        // Create repository with source context
        var repo = factory.Create<TEntity, TKey>(sp, source);

        // Wrap with facade for identity management and schema guard
        var manager = sp.GetRequiredService<IAggregateIdentityManager>();
        var guard = sp.GetRequiredService<EntitySchemaGuard<TEntity, TKey>>();
        var facade = new RepositoryFacade<TEntity, TKey>(repo, manager, guard);

        var decorated = ApplyDecorators(typeof(TEntity), typeof(TKey), facade, sp);

        _cache[key] = decorated;
        return decorated;
    }

    /// <inheritdoc />
    public Direct.IDirectSession Direct(string? source = null, string? adapter = null)
    {
        var svc = sp.GetService<Direct.IDirectDataService>()
            ?? throw new InvalidOperationException("IDirectDataService not registered. AddKoanDataDirect() required.");
        return svc.Direct(source, adapter);
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

    private static IDataRepository<TEntity, TKey> ApplyDecorators<TEntity, TKey>(
        Type entityType,
        Type keyType,
        IDataRepository<TEntity, TKey> repository,
        IServiceProvider services)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var decorators = services.GetService<IEnumerable<Decorators.IDataRepositoryDecorator>>();
        if (decorators is null)
        {
            return repository;
        }

        object current = repository;

        foreach (var decorator in decorators)
        {
            var result = decorator.TryDecorate(entityType, keyType, current, services);
            if (result is not null)
            {
                current = result;
            }
        }

        return (IDataRepository<TEntity, TKey>)current;
    }

}
