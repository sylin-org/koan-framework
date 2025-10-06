using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Core;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Hosting;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Redis;
using Koan.Data.Connector.Redis.Infrastructure;

namespace Koan.Cache.Adapter.Redis;

public sealed class RedisCacheAdapterRegistrar : ICacheAdapterRegistrar
{
    public string Name => "redis";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddKoanOptions<RedisCacheAdapterOptions>(configuration, CacheConstants.Configuration.Redis.Section);
        services.PostConfigure<RedisCacheAdapterOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.KeyPrefix))
            {
                opts.KeyPrefix = "cache:";
            }

            if (string.IsNullOrWhiteSpace(opts.TagPrefix))
            {
                opts.TagPrefix = "cache:tag:";
            }

            if (string.IsNullOrWhiteSpace(opts.ChannelName))
            {
                opts.ChannelName = "koan-cache";
            }
        });

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetService<ILogger<RedisCacheAdapterRegistrar>>();
            var connectionString = ResolveConnectionString(sp);
            logger?.LogDebug("Connecting Redis cache adapter to {ConnectionString}", KoanEnv.IsDevelopment ? connectionString : Koan.Core.Redaction.DeIdentify(connectionString));
            return ConnectionMultiplexer.Connect(connectionString);
        });

        services.AddSingleton<RedisCacheStore>();
        services.AddSingleton<ICacheStore>(sp => sp.GetRequiredService<RedisCacheStore>());
        services.AddHostedService<RedisInvalidationListener>();
        services.AddSingleton(new CacheAdapterDescriptor(Name, GetType(), "Redis distributed cache adapter"));
    }

    private static string ResolveConnectionString(IServiceProvider sp)
    {
        var cacheOptions = sp.GetRequiredService<IOptions<RedisCacheAdapterOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(cacheOptions.Configuration))
        {
            return cacheOptions.Configuration;
        }

        var dataOptions = sp.GetService<IOptions<RedisOptions>>()?.Value;
        if (dataOptions is not null && !string.IsNullOrWhiteSpace(dataOptions.ConnectionString) && !string.Equals(dataOptions.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return dataOptions.ConnectionString;
        }

        var coordinator = sp.GetService<IServiceDiscoveryCoordinator>();
        if (coordinator is not null)
        {
            try
            {
                var result = coordinator.DiscoverServiceAsync(Constants.Discovery.WellKnownServiceName, new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode
                }).ConfigureAwait(false).GetAwaiter().GetResult();
                if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl))
                {
                    return result.ServiceUrl!;
                }
            }
            catch
            {
                // fall through to defaults
            }
        }

        return KoanEnv.InContainer ? Constants.Discovery.DefaultCompose : Constants.Discovery.DefaultLocal;
    }
}
