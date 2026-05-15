using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapters;
using Koan.Cache.Adapters.Memory;
using Koan.Cache.Decorators;
using Koan.Cache.Diagnostics;
using Koan.Cache.Options;
using Koan.Cache.Policies;
using Koan.Cache.Scope;
using Koan.Cache.Serialization;
using Koan.Cache.Stores;
using Koan.Cache.Topology;
using Koan.Core.Modules;
using Koan.Core.Singleflight;
using Koan.Data.Core.Decorators;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Extensions;

public static class CacheServiceCollectionExtensions
{
    /// <summary>
    /// Wire the cache pillar: storage (Memory L1 default), topology resolver, layered cache,
    /// client, serializers, scope, singleflight, policy registry, and the data-repository
    /// decorator that auto-applies <c>[Cacheable]</c> policies.
    /// </summary>
    public static IServiceCollection AddKoanCache(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<CacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Options
        if (configuration is not null)
        {
            services.AddKoanOptions<CacheOptions>(configuration, CacheConstants.Configuration.Section, configure);
        }
        else
        {
            services.AddKoanOptions<CacheOptions>(CacheConstants.Configuration.Section);
            if (configure is not null) services.PostConfigure(configure);
        }

        // Cross-cutting primitives
        services.AddKoanSingleflight();
        services.TryAddSingleton<ICacheScopeAccessor, CacheScopeAccessor>();
        services.TryAddSingleton<CacheInstrumentation>();

        // Serializers (registered as enumerable; resolved by content kind + type at runtime)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, JsonCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, StringCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, BinaryCacheSerializer>());

        // Default built-in L1 store: Memory. Adapters add their own stores via
        // TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore>, sp => ...).
        services.AddMemoryCache();
        services.AddKoanOptions<MemoryCacheAdapterOptions>(CacheConstants.Configuration.Memory.Section);
        services.TryAddSingleton<MemoryCacheStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheStore>(sp => sp.GetRequiredService<MemoryCacheStore>()));

        // Topology
        services.TryAddSingleton<CacheStoreRegistry>();
        services.TryAddSingleton<ICacheStoreRegistry>(sp => sp.GetRequiredService<CacheStoreRegistry>());
        services.TryAddSingleton<CacheTopologyResolver>();
        services.TryAddSingleton<CacheTopology>(sp =>
        {
            var registry = sp.GetRequiredService<ICacheStoreRegistry>();
            var resolver = sp.GetRequiredService<CacheTopologyResolver>();
            var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
            return resolver.Resolve(registry.Stores, opts);
        });
        services.TryAddSingleton<LayeredCache>();

        // Client (consumes LayeredCache; ICacheClient = ICacheReader + ICacheWriter)
        services.TryAddSingleton<CacheClient>();
        services.TryAddSingleton<ICacheClient>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheReader>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheWriter>(sp => sp.GetRequiredService<CacheClient>());

        // Policies + repository decorator
        services.TryAddSingleton<CachePolicyRegistry>();
        services.TryAddSingleton<ICachePolicyRegistry>(sp => sp.GetRequiredService<CachePolicyRegistry>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataRepositoryDecorator, CacheRepositoryDecorator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CacheOptions>, CacheOptionsValidator>());

        services.AddHostedService<CachePolicyBootstrapper>();

        return services;
    }

    /// <summary>
    /// Legacy adapter registration shim. New adapters self-register their <c>ICacheStore</c>
    /// via their own <c>KoanAutoRegistrar</c>; this method is preserved as a no-op for
    /// back-compat with older registrars (Sqlite, Redis adapters from before M2).
    /// </summary>
    [Obsolete("New adapters register ICacheStore directly. This shim only records the descriptor.")]
    public static IServiceCollection AddKoanCacheAdapter(this IServiceCollection services, string name, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Adapter name must be provided.", nameof(name));

        services.AddSingleton(new CacheAdapterDescriptor(name, typeof(CacheAdapterResolver)));
        return services;
    }

    private sealed class CacheOptionsValidator : IConfigureOptions<CacheOptions>
    {
        private readonly ILogger<CacheOptionsValidator> _logger;

        public CacheOptionsValidator(ILogger<CacheOptionsValidator> logger) => _logger = logger;

        public void Configure(CacheOptions options)
        {
            if (options.DefaultSingleflightTimeout <= TimeSpan.Zero)
            {
                _logger.LogWarning("CacheOptions.DefaultSingleflightTimeout was {Timeout}. Resetting to 2 seconds.", options.DefaultSingleflightTimeout);
                options.DefaultSingleflightTimeout = TimeSpan.FromSeconds(2);
            }
        }
    }
}
