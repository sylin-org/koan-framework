using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapters;
using Koan.Cache.Adapters.Memory;
using Koan.Cache.Options;
using Koan.Cache.Policies;
using Koan.Cache.Decorators;
using Koan.Cache.Scope;
using Koan.Cache.Serialization;
using Koan.Cache.Singleflight;
using Koan.Cache.Stores;
using Koan.Cache.Diagnostics;
using Koan.Core.Modules;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Core.Decorators;

namespace Koan.Cache.Extensions;

public static class CacheServiceCollectionExtensions
{
    public static IServiceCollection AddKoanCache(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<CacheOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration is not null)
        {
            services.AddKoanOptions<CacheOptions>(configuration, CacheConstants.Configuration.Section, configure);
        }
        else
        {
            services.AddKoanOptions<CacheOptions>(CacheConstants.Configuration.Section);
            if (configure is not null)
            {
                services.PostConfigure(configure);
            }
        }

        services.TryAddSingleton<ICacheScopeAccessor, CacheScopeAccessor>();
        services.TryAddSingleton<CacheSingleflightRegistry>();
    services.TryAddSingleton<CacheClient>();
    services.TryAddSingleton<ICacheClient>(sp => sp.GetRequiredService<CacheClient>());
    services.TryAddSingleton<ICacheReader>(sp => sp.GetRequiredService<CacheClient>());
    services.TryAddSingleton<ICacheWriter>(sp => sp.GetRequiredService<CacheClient>());
    services.TryAddSingleton<CachePolicyRegistry>();
    services.TryAddSingleton<ICachePolicyRegistry>(sp => sp.GetRequiredService<CachePolicyRegistry>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataRepositoryDecorator, CacheRepositoryDecorator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, JsonCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, StringCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, BinaryCacheSerializer>());

        services.TryAddSingleton<CacheInstrumentation>();

        // Store registry: individual adapters register concrete store singletons.
        // LayeredCacheStore reads from the registry and becomes the single ICacheStore.
        services.TryAddSingleton<CacheStoreRegistry>();
        services.TryAddSingleton<ICacheStoreRegistry>(sp => sp.GetRequiredService<CacheStoreRegistry>());
        services.TryAddSingleton<LayeredCacheStore>();

        // Default fallback: if no explicit adapter is registered, auto-register the bundled in-memory provider.
        // This ensures "Reference = Intent" — just adding Koan.Cache gets you a working cache.
        services.AddMemoryCache();
        services.AddKoanOptions<MemoryCacheAdapterOptions>(CacheConstants.Configuration.Memory.Section);
        services.TryAddSingleton<MemoryCacheStore>();

        // The ICacheStore singleton is the LayeredCacheStore, which orchestrates L1/L2.
        // Before returning it, we populate the registry with all known concrete stores.
        services.TryAddSingleton<ICacheStore>(sp =>
        {
            var registry = sp.GetRequiredService<CacheStoreRegistry>();
            PopulateStoreRegistry(sp, registry);
            return sp.GetRequiredService<LayeredCacheStore>();
        });

        services.PostConfigure<CacheOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.Provider))
            {
                opts.Provider = "memory";
            }
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CacheOptions>, CacheOptionsValidator>());

        services.AddHostedService<CachePolicyBootstrapper>();

        return services;
    }

    public static IServiceCollection AddKoanCacheAdapter(this IServiceCollection services, string name, IConfiguration? configuration = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Adapter name must be provided.", nameof(name));
        }

        var registrar = CacheAdapterResolver.Resolve(name);
        var config = configuration ?? new ConfigurationManager();
        registrar.Register(services, config);

        services.AddSingleton(new CacheAdapterDescriptor(name, registrar.GetType()));

        services.PostConfigure<CacheOptions>(opts =>
        {
            opts.Provider = name;
        });

        return services;
    }

    /// <summary>
    /// Populates the store registry with all known concrete cache stores.
    /// Each adapter registers its concrete type as a singleton; we resolve and register them here
    /// so LayeredCacheStore can discover L1/L2 tiers via the registry.
    /// </summary>
    private static void PopulateStoreRegistry(IServiceProvider sp, CacheStoreRegistry registry)
    {
        // Always register the built-in memory store
        var memoryStore = sp.GetService<MemoryCacheStore>();
        if (memoryStore is not null)
        {
            registry.Register(memoryStore);
        }

        // Discover additional stores from adapter descriptors
        var descriptors = sp.GetServices<CacheAdapterDescriptor>();
        foreach (var descriptor in descriptors)
        {
            // Skip memory — already handled above
            if (descriptor.Name.Equals("memory", StringComparison.OrdinalIgnoreCase))
                continue;

            // Resolve the concrete store by well-known naming convention:
            // adapter descriptors carry the adapter name, and the concrete store
            // is already registered as a singleton by the adapter registrar.
            var store = ResolveStoreForAdapter(sp, descriptor.Name);
            if (store is not null)
            {
                registry.Register(store);
            }
        }
    }

    /// <summary>
    /// Resolves the concrete ICacheStore for a named adapter.
    /// Adapters register their concrete store type as a singleton (e.g., RedisCacheStore).
    /// We look it up via IEnumerable of all registered ICacheStore-assignable singletons.
    /// </summary>
    private static ICacheStore? ResolveStoreForAdapter(IServiceProvider sp, string adapterName)
    {
        // Try to find a registered store whose ProviderName matches the adapter name.
        // We cannot resolve IEnumerable<ICacheStore> because that would trigger the
        // LayeredCacheStore factory (circular). Instead, resolve well-known concrete types
        // by convention. Additional adapters can extend this via the registry directly.
        return adapterName.ToLowerInvariant() switch
        {
            "redis" => ResolveByType(sp, "Koan.Cache.Adapter.Redis.Stores.RedisCacheStore"),
            _ => null
        };
    }

    private static ICacheStore? ResolveByType(IServiceProvider sp, string typeName)
    {
        // Try all loaded assemblies for the type
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type is not null)
            {
                return sp.GetService(type) as ICacheStore;
            }
        }

        return null;
    }

    private sealed class CacheOptionsValidator : IConfigureOptions<CacheOptions>
    {
        private readonly ILogger<CacheOptionsValidator> _logger;

        public CacheOptionsValidator(ILogger<CacheOptionsValidator> logger)
        {
            _logger = logger;
        }

        public void Configure(CacheOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Provider))
            {
                options.Provider = "memory";
            }

            if (options.DefaultSingleflightTimeout <= TimeSpan.Zero)
            {
                _logger.LogWarning("CacheOptions.DefaultSingleflightTimeout was {Timeout}. Resetting to 2 seconds.", options.DefaultSingleflightTimeout);
                options.DefaultSingleflightTimeout = TimeSpan.FromSeconds(2);
            }
        }
    }
}
