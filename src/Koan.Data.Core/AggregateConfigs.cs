using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Core;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Koan.Core.Hosting.App;

namespace Koan.Data.Core;

public static class AggregateConfigs
{
    private static ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<(Type EntityType, Type KeyType), object>> _configsByProvider = new();

    public static AggregateConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(sp);

        var key = (typeof(TEntity), typeof(TKey));
        var cache = Volatile.Read(ref _configsByProvider).GetValue(
            sp,
            static _ => new ConcurrentDictionary<(Type EntityType, Type KeyType), object>());

        var config = (AggregateConfig<TEntity, TKey>)cache.GetOrAdd(key, _ =>
        {
            var idSpec = AggregateMetadata.GetIdSpec(typeof(TEntity));
            return new AggregateConfig<TEntity, TKey>(idSpec, sp);
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
    /// The current host's catalog retains type facts only. Provider selection, repositories, and services
    /// remain isolated to the provider supplied to <see cref="Get{TEntity,TKey}"/>.
    /// </remarks>
    public static IReadOnlyCollection<(Type EntityType, Type KeyType)> GetRegisteredTypes()
    {
        var provider = AppHost.Current;
        return provider is not null && Volatile.Read(ref _configsByProvider).TryGetValue(provider, out var configs)
            ? configs.Keys.ToArray()
            : [];
    }

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
    }
}

// Public-facing shim to access per-entity bags without exposing internal types broadly
