using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Coherence;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Redis;
using Koan.Data.Connector.Redis.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Initialization;

/// <summary>
/// Reference = Intent: referencing <c>Koan.Cache.Adapter.Redis</c> auto-registers BOTH the
/// Redis storage (<see cref="RedisCacheStore"/> as the L2 tier) AND the Redis pub/sub
/// coherence channel (<see cref="RedisCoherenceChannel"/>). The coherence coordinator then
/// activates automatically in <c>CoherenceMode.AutoDetect</c>.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Redis";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Options
        services.AddKoanOptions<RedisCacheAdapterOptions>(CacheConstants.Configuration.Redis.Section);
        services.AddKoanOptions<RedisCoherenceChannelOptions>(CacheConstants.Configuration.Redis.Section);
        services.PostConfigure<RedisCacheAdapterOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.KeyPrefix)) opts.KeyPrefix = "cache:";
            if (string.IsNullOrWhiteSpace(opts.TagPrefix)) opts.TagPrefix = "cache:tag:";
            if (string.IsNullOrWhiteSpace(opts.Configuration) || string.Equals(opts.Configuration, "auto", StringComparison.OrdinalIgnoreCase))
                opts.Configuration = ""; // marker for runtime resolution
        });
        services.PostConfigure<RedisCoherenceChannelOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ChannelName)) opts.ChannelName = "koan-cache";
        });

        // Connection multiplexer — shared between RedisCacheStore (storage) and
        // RedisCoherenceChannel (pub/sub). Single connection, dual purpose.
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetService<ILogger<KoanAutoRegistrar>>();
            var connectionString = ResolveConnectionString(sp);
            logger?.LogDebug("Connecting Redis cache adapter to {ConnectionString}",
                KoanEnv.IsDevelopment ? connectionString : Koan.Core.Redaction.DeIdentify(connectionString));
            return ConnectionMultiplexer.Connect(connectionString);
        });

        // Storage: RedisCacheStore as Remote (L2) tier.
        services.TryAddSingleton<RedisCacheStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheStore>(sp => sp.GetRequiredService<RedisCacheStore>()));

        // Coherence: RedisCoherenceChannel as ICacheCoherenceChannel.
        services.TryAddSingleton<RedisCoherenceChannel>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheCoherenceChannel>(sp => sp.GetRequiredService<RedisCoherenceChannel>()));

        // Service discovery for orchestration (parity with data-connector discovery).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Koan.Data.Connector.Redis.Discovery.RedisDiscoveryAdapter>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var configuration = Configuration.Read(cfg, CacheConstants.Configuration.Redis.Configuration, "auto");
        var channel = Configuration.Read(cfg, CacheConstants.Configuration.Redis.ChannelName, "koan-cache");
        var prefix = Configuration.Read(cfg, CacheConstants.Configuration.Redis.KeyPrefix, "cache:");

        module.AddSetting("CacheStore", "redis (Remote, [ProviderPriority(100)])");
        module.AddSetting("CoherenceChannel", "redis-pubsub ([ProviderPriority(100)])");
        module.AddSetting("RedisConfiguration", configuration ?? "auto");
        module.AddSetting("ChannelName", channel ?? "koan-cache");
        module.AddSetting("KeyPrefix", prefix ?? "cache:");
    }

    private static string ResolveConnectionString(IServiceProvider sp)
    {
        var cacheOptions = sp.GetRequiredService<IOptions<RedisCacheAdapterOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(cacheOptions.Configuration))
            return cacheOptions.Configuration;

        var dataOptions = sp.GetService<IOptions<RedisOptions>>()?.Value;
        if (dataOptions is not null && !string.IsNullOrWhiteSpace(dataOptions.ConnectionString) &&
            !string.Equals(dataOptions.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return dataOptions.ConnectionString;
        }

        var coordinator = sp.GetService<IServiceDiscoveryCoordinator>();
        if (coordinator is not null)
        {
            try
            {
                var result = coordinator.DiscoverService(Constants.Discovery.WellKnownServiceName, new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode
                }).GetAwaiter().GetResult();
                if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl))
                    return result.ServiceUrl!;
            }
            catch
            {
                // fall through to defaults
            }
        }

        return KoanEnv.InContainer ? Constants.Discovery.DefaultCompose : Constants.Discovery.DefaultLocal;
    }
}
