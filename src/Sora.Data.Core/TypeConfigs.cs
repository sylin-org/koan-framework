using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Metadata;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Sora.Data.Core.Configuration;

internal static class AggregateConfigs
{
    private static readonly ConcurrentDictionary<(Type, Type), object> Cache = new();

    public static AggregateConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (Cache.TryGetValue(key, out var existing)) return (AggregateConfig<TEntity, TKey>)existing;

        var provider = ResolveProvider(typeof(TEntity)) ?? DefaultProvider(sp);
        var idSpec = AggregateMetadata.GetIdSpec(typeof(TEntity));
        var cfg = new AggregateConfig<TEntity, TKey>(provider, idSpec, sp);
        Cache[key] = cfg;
        return cfg;
    }

    // Testing/host reset: clear cached configs to avoid cross-root contamination
    internal static void Reset() => Cache.Clear();

    private static string? ResolveProvider(Type aggregateType)
    {
        var attr = (DataAdapterAttribute?)Attribute.GetCustomAttribute(aggregateType, typeof(DataAdapterAttribute));
        return attr?.Provider;
    }

    private static string DefaultProvider(IServiceProvider sp)
    {
        var factories = sp.GetServices<IDataAdapterFactory>().ToList();
        if (factories.Count == 0) return "json"; // safe fallback

        // Rank by ProviderPriorityAttribute (higher wins), then by type name for stability
        var ranked = factories
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(Sora.Data.Abstractions.ProviderPriorityAttribute), inherit: false).FirstOrDefault() as Sora.Data.Abstractions.ProviderPriorityAttribute)?.Priority ?? 0,
                Name = f.GetType().Name
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosen = ranked.First().Factory.GetType().Name;
        const string suffix = "AdapterFactory";
        if (chosen.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) chosen = chosen[..^suffix.Length];
        return chosen.ToLowerInvariant();
    }
}

// Public-facing shim to access per-entity bags without exposing internal types broadly
public static class AggregateBags
{
    public static TBag GetOrAdd<TEntity, TKey, TBag>(IServiceProvider sp, string key, Func<TBag> factory)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull
        where TBag : class
    {
        var cfg = AggregateConfigs.Get<TEntity, TKey>(sp);
        return cfg.GetOrAddBag(key, factory);
    }
}

internal sealed class AggregateConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public string Provider { get; }
    public AggregateMetadata.IdSpec? Id { get; }

    private readonly Lazy<IDataRepository<TEntity, TKey>> _repo;
    public IDataRepository<TEntity, TKey> Repository => _repo.Value;

    // Optional per-entity cache for provider/dialect-specific rendered commands and binders
    private readonly ConcurrentDictionary<string, object> _bags = new();
    public TBag GetOrAddBag<TBag>(string key, Func<TBag> factory) where TBag : class
        => (TBag)_bags.GetOrAdd(key, _ => factory()!);
    internal System.Collections.Generic.IEnumerable<(string key, object value)> EnumerateBags()
        => _bags.Select(kvp => (kvp.Key, kvp.Value));

    internal AggregateConfig(string provider, AggregateMetadata.IdSpec? id, IServiceProvider sp)
    {
        Provider = provider;
        Id = id;
        _repo = new Lazy<IDataRepository<TEntity, TKey>>(() =>
        {
            var factories = sp.GetServices<IDataAdapterFactory>();
            var factory = factories.FirstOrDefault(f => f.CanHandle(provider))
                ?? throw new InvalidOperationException($"No data adapter factory for provider '{provider}'");
            var repo = factory.Create<TEntity, TKey>(sp);
            var manager = sp.GetRequiredService<IAggregateIdentityManager>();
            // Decorate with RepositoryFacade for cross-cutting concerns
            return new RepositoryFacade<TEntity, TKey>(repo, manager);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
