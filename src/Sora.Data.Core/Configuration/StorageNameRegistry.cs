using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Core.Configuration;

/// <summary>
/// Central registry to resolve and cache storage names per (entity, key) into AggregateBags.
/// </summary>
public static class StorageNameRegistry
{
    private static string BagKey(string provider, string? set)
        => set is null || set.Length == 0 || string.Equals(set, "root", System.StringComparison.OrdinalIgnoreCase)
            ? $"name:{provider}:root"
            : $"name:{provider}:{set}";

    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = AggregateConfigs.Get<TEntity, TKey>(sp);
        var provider = cfg.Provider;
        var set = Sora.Data.Core.DataSetContext.Current;
        var key = BagKey(provider, set);
        return AggregateBags.GetOrAdd<TEntity, TKey, string>(sp, key, () =>
        {
            // Resolve the provider-specific defaults
            var providers = sp.GetServices<INamingDefaultsProvider>();
            INamingDefaultsProvider? defaultsProvider = providers.FirstOrDefault(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase))
                ?? providers.FirstOrDefault();
            if (defaultsProvider is null)
            {
                // No registered defaults provider; fall back to the DI resolver with built-in defaults
                var diFallback = sp.GetRequiredService<IStorageNameResolver>();
                // Prefer global fallback options if configured
                var fallback = sp.GetService<IOptions<Sora.Data.Core.Naming.NamingFallbackOptions>>()?.Value;
                var convFallback = fallback is not null
                    ? new StorageNameResolver.Convention(fallback.Style, fallback.Separator, fallback.Casing)
                    : new StorageNameResolver.Convention(StorageNamingStyle.EntityType, ".", NameCasing.AsIs);
                var baseName = StorageNameSelector.ResolveName(repository: null, diFallback, typeof(TEntity), convFallback, adapterOverride: null);
                return AppendSet(baseName, set);
            }
            var diResolver = sp.GetRequiredService<IStorageNameResolver>();
            var conv = defaultsProvider.GetConvention(sp);
            var overrideFn = defaultsProvider.GetAdapterOverride(sp);
            var resolved = StorageNameSelector.ResolveName(repository: null, diResolver, typeof(TEntity), conv, overrideFn);
            return AppendSet(resolved, set);
        });
    }

    private static string AppendSet(string baseName, string? set)
    {
        if (string.IsNullOrWhiteSpace(set) || string.Equals(set, "root", System.StringComparison.OrdinalIgnoreCase))
            return baseName;
        return baseName + "#" + set;
    }
}
