using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Core;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Koan.Data.Core;

public static class AggregateConfigs
{
    private static ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<(Type EntityType, Type KeyType), object>> _configsByProvider = new();
    private static readonly ConcurrentDictionary<(Type EntityType, Type KeyType), byte> RegisteredTypes = new();

    public static AggregateConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(sp);

        var key = (typeof(TEntity), typeof(TKey));
        RegisteredTypes.TryAdd(key, 0);
        var cache = Volatile.Read(ref _configsByProvider).GetValue(
            sp,
            static _ => new ConcurrentDictionary<(Type EntityType, Type KeyType), object>());

        var config = (AggregateConfig<TEntity, TKey>)cache.GetOrAdd(key, _ =>
        {
            var provider = ResolveProvider(typeof(TEntity)) ?? DefaultProvider(sp);
            var idSpec = AggregateMetadata.GetIdSpec(typeof(TEntity));
            return new AggregateConfig<TEntity, TKey>(provider, idSpec, sp);
        });

        sp.GetService<DataDiagnostics>()?.Observe(new EntityConfigInfo(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            typeof(TKey).FullName ?? typeof(TKey).Name,
            config.Provider,
            config.Id?.Prop.Name));

        return config;
    }

    /// <summary>
    /// Gets the provider-free entity and key types observed by aggregate configuration.
    /// </summary>
    /// <remarks>
    /// This process-wide discovery surface retains type facts only. Provider selection, repositories,
    /// and services remain isolated to the provider supplied to <see cref="Get{TEntity,TKey}"/>.
    /// </remarks>
    public static IReadOnlyCollection<(Type EntityType, Type KeyType)> GetRegisteredTypes()
        => RegisteredTypes.Keys.ToArray();

    /// <summary>
    /// Clears aggregate configuration and discovery state used by test matrices.
    /// </summary>
    /// <remarks>
    /// Runtime correctness does not require this reset: configuration caches are partitioned by
    /// service-provider identity and release with their provider.
    /// </remarks>
    public static void Reset()
    {
        Interlocked.Exchange(
            ref _configsByProvider,
            new ConditionalWeakTable<
                IServiceProvider,
                ConcurrentDictionary<(Type EntityType, Type KeyType), object>>());
        RegisteredTypes.Clear();
    }

    private static string? ResolveProvider(Type aggregateType)
    {
        // Prefer explicit SourceAdapter if present, then fall back to legacy DataAdapter
        var src = (SourceAdapterAttribute?)Attribute.GetCustomAttribute(aggregateType, typeof(SourceAdapterAttribute));
        if (src is not null && !string.IsNullOrWhiteSpace(src.Provider)) return src.Provider;
        var data = (DataAdapterAttribute?)Attribute.GetCustomAttribute(aggregateType, typeof(DataAdapterAttribute));
        return data?.Provider;
    }

    private static string DefaultProvider(IServiceProvider sp)
    {
        var factories = sp.GetServices<IDataAdapterFactory>().ToList();
        if (factories.Count == 0) throw new InvalidOperationException("No IDataAdapterFactory instances registered. Ensure services.AddKoanDataCore() has been called and a data adapter module is referenced.");

        // Rank by ProviderPriorityAttribute (higher wins), then by type name for stability
        var ranked = factories
            .Select(f => new
            {
                Factory = f,
                Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), inherit: false).FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
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
