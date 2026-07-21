using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapters.Memory;
using Koan.Cache.Coherence;
using Koan.Cache.Decorators;
using Koan.Cache.Diagnostics;
using Koan.Cache.Entity;
using Koan.Cache.Identity;
using Koan.Cache.Options;
using Koan.Cache.Policies;
using Koan.Cache.Scope;
using Koan.Cache.Serialization;
using Koan.Cache.Stores;
using Koan.Cache.Topology;
using Koan.Communication;
using Koan.Communication.Signals;
using Koan.Core.Modules;
using Koan.Core.Concurrency;
using Koan.Data.Core.Decorators;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Initialization;

internal static class CacheServices
{
    /// <summary>
    /// Wire the cache pillar: storage (Memory L1 default), topology resolver, layered cache,
    /// client, serializers, scope, singleflight, policy registry, and the data-repository
    /// decorator that auto-applies <c>[Cacheable]</c> policies.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddKoanCommunication();

        // Options
        services.AddKoanOptions<CacheOptions>(CacheConstants.Configuration.Section);

        // Cross-cutting primitives
        services.AddKoanKeyedLeaseGate();
        services.TryAddSingleton<ICacheScopeAccessor, CacheScopeAccessor>();
        services.TryAddSingleton<CacheInstrumentation>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            Koan.Core.Semantics.Segmentation.ISegmentationRealization,
            CacheIdentityPlan>());
        services.TryAddSingleton(sp => sp
            .GetServices<Koan.Core.Semantics.Segmentation.ISegmentationRealization>()
            .OfType<CacheIdentityPlan>()
            .Single());

        // Serializers (registered as enumerable; resolved by content kind + type at runtime)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, JsonCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, StringCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, BinaryCacheSerializer>());

        // Default built-in L1 store: Memory. Adapters append stores through standard .NET DI.
        services.AddMemoryCache();
        services.AddKoanOptions<MemoryCacheAdapterOptions>(CacheConstants.Configuration.Memory.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore, MemoryCacheStore>());

        // Topology
        services.TryAddSingleton<CacheTopology>();
        services.TryAddSingleton<LayeredCache>();

        // Cache owns invalidation meaning; Communication owns local/remote carriage and provider election.
        services.TryAddSingleton<NodeIdProvider>();
        services.TryAddSingleton<CoherenceCoordinator>();
        services.AddFrameworkBroadcast<CacheInvalidationSignal, CoherenceCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<CoherenceCoordinator>());

        // Client (consumes LayeredCache; ICacheClient = ICacheReader + ICacheWriter)
        services.TryAddSingleton<CacheClient>();
        services.TryAddSingleton<ICacheSubjectClient>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheClient>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheReader>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheWriter>(sp => sp.GetRequiredService<CacheClient>());

        // Policies + repository decorator
        services.TryAddSingleton<CachePolicyRegistry>();
        services.TryAddSingleton<ICachePolicyRegistry>(sp => sp.GetRequiredService<CachePolicyRegistry>());
        services.TryAddSingleton<EntityCachePlan>();
        services.TryAddSingleton<EntityCacheEvictionCoordinator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataRepositoryDecorator, CacheRepositoryDecorator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CacheOptions>, CacheOptionsValidator>());

        services.TryAddSingleton<CachePolicyBootstrapper>();
        services.AddHostedService(sp => sp.GetRequiredService<CachePolicyBootstrapper>());

        // Health check — registered against the standard ASP.NET Core IHealthCheck registry.
        // Kubernetes / Aspire readiness probes pick it up automatically.
        services.AddHealthChecks().AddCheck<CacheHealthCheck>(
            name: "koan-cache",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: new[] { "cache", "koan" });

    }

    private sealed class CacheOptionsValidator : IConfigureOptions<CacheOptions>
    {
        private readonly ILogger<CacheOptionsValidator> _logger;

        public CacheOptionsValidator(ILogger<CacheOptionsValidator> logger) => _logger = logger;

        public void Configure(CacheOptions options)
        {
            if (!Enum.IsDefined(options.DefaultTier))
                throw new InvalidOperationException(
                    $"Koan Cache configuration 'Cache:DefaultTier' has unsupported value '{options.DefaultTier}'. " +
                    "Choose Layered, LocalOnly, or RemoteOnly.");
            if (options.DefaultTtlSeconds < 0)
                throw new InvalidOperationException(
                    "Koan Cache configuration 'Cache:DefaultTtlSeconds' cannot be negative; use 0 for no expiration.");
            if (options.DefaultL1TtlSeconds is < 0)
                throw new InvalidOperationException(
                    "Koan Cache configuration 'Cache:DefaultL1TtlSeconds' cannot be negative; omit it to derive the Local TTL.");
            if (options.DefaultTtlSeconds > 0 && options.DefaultL1TtlSeconds > options.DefaultTtlSeconds)
                throw new InvalidOperationException(
                    "Koan Cache configuration 'Cache:DefaultL1TtlSeconds' cannot exceed 'Cache:DefaultTtlSeconds'.");
            if (options.DefaultSingleflightTimeout <= TimeSpan.Zero)
            {
                _logger.LogWarning("CacheOptions.DefaultSingleflightTimeout was {Timeout}. Resetting to 2 seconds.", options.DefaultSingleflightTimeout);
                options.DefaultSingleflightTimeout = TimeSpan.FromSeconds(2);
            }
        }
    }
}
