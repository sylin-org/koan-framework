using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

/// <summary>Host-owned vector configuration resolution for Entity types.</summary>
internal static class VectorConfigs
{
    private static ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<(Type EntityType, Type KeyType), object>> _configsByProvider = new();

    internal static VectorConfig<TEntity, TKey> Get<TEntity, TKey>(IServiceProvider services)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        var cache = Volatile.Read(ref _configsByProvider).GetValue(
            services,
            static _ => new ConcurrentDictionary<(Type, Type), object>());
        return (VectorConfig<TEntity, TKey>)cache.GetOrAdd(
            (typeof(TEntity), typeof(TKey)),
            _ => new VectorConfig<TEntity, TKey>(ResolveProvider(typeof(TEntity), services), services));
    }

    internal static void Reset() => Interlocked.Exchange(
        ref _configsByProvider,
        new ConditionalWeakTable<
            IServiceProvider,
            ConcurrentDictionary<(Type EntityType, Type KeyType), object>>());

    private static string ResolveProvider(Type entityType, IServiceProvider services)
    {
        var providers = services.GetRequiredService<IVectorProviderResolver>();
        var attribute = (VectorAdapterAttribute?)Attribute.GetCustomAttribute(
            entityType,
            typeof(VectorAdapterAttribute));
        if (!string.IsNullOrWhiteSpace(attribute?.Provider))
        {
            return providers.Find(attribute.Provider)?.Provider
                ?? throw Unavailable(entityType, attribute.Provider, providers);
        }

        return providers.SelectAutomatic()?.Provider
            ?? throw new InvalidOperationException(
                $"No automatic vector provider is available for Entity '{entityType.Name}'. " +
                "Reference a vector connector or add an exact VectorAdapter decoration.");
    }

    private static InvalidOperationException Unavailable(
        Type entityType,
        string requested,
        IVectorProviderResolver providers) => new(
        $"Entity '{entityType.Name}' requires vector provider '{requested}', but it is unavailable. " +
        $"Referenced vector providers: {(providers.AvailableProviderIds.Count == 0 ? "none" : string.Join(", ", providers.AvailableProviderIds))}. " +
        "Correct the VectorAdapter decoration or reference the intended connector; Koan will not substitute an unrelated provider.");
}
