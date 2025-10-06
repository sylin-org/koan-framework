using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.Redis;
using Koan.Data.Connector.Redis.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Hosting;

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
            var cacheOptions = sp.GetRequiredService<IOptions<RedisCacheAdapterOptions>>().Value;
            var logger = sp.GetService<ILogger<RedisCacheAdapterRegistrar>>();

            var connectionString = cacheOptions.Configuration;
            if (string.IsNullOrWhiteSpace(connectionString) || string.Equals(connectionString, "auto", StringComparison.OrdinalIgnoreCase))
            {
                var dataOptions = sp.GetService<IOptions<RedisOptions>>();
                var candidate = dataOptions?.Value.ConnectionString;
                if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    connectionString = candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(connectionString) || string.Equals(connectionString, "auto", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = KoanEnv.InContainer ? Constants.Discovery.DefaultCompose : Constants.Discovery.DefaultLocal;
            }

            logger?.LogDebug("Connecting Redis cache adapter to {ConnectionString}", connectionString);

            return ConnectionMultiplexer.Connect(connectionString);
        });

        services.AddSingleton<RedisCacheStore>();
        services.AddSingleton<ICacheStore>(sp => sp.GetRequiredService<RedisCacheStore>());
        services.AddHostedService<RedisInvalidationListener>();
        services.AddSingleton(new CacheAdapterDescriptor(Name, GetType(), "Redis distributed cache adapter"));
    }
}
