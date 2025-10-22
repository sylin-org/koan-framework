using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Redis;
using Koan.Data.Connector.Redis.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Cache.Adapter.Redis.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Redis";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheAdapter("redis");

        // Ensure cache module reuses data connector discovery so both layers resolve identical endpoints.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Koan.Data.Connector.Redis.Discovery.RedisDiscoveryAdapter>());
        services.PostConfigure<Options.RedisCacheAdapterOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.Configuration) || string.Equals(opts.Configuration, "auto", StringComparison.OrdinalIgnoreCase))
            {
                opts.Configuration = string.Empty; // marker for runtime resolution
            }
        });
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var configuration = Configuration.Read(cfg, CacheConstants.Configuration.Redis.Configuration, "auto");
        var channel = Configuration.Read(cfg, CacheConstants.Configuration.Redis.ChannelName, "koan-cache");
        var prefix = Configuration.Read(cfg, CacheConstants.Configuration.Redis.KeyPrefix, "cache:");

    module.AddSetting("CacheStore.Selected", "redis");
    module.AddSetting("CacheStore.Candidates", "memory, redis, custom");
    module.AddSetting("CacheStore.Rationale", "Reference = redis adapter package");
        module.AddSetting("RedisConfiguration", configuration ?? "auto");
        module.AddSetting("ChannelName", channel ?? "koan-cache");
        module.AddSetting("KeyPrefix", prefix ?? "cache:");
    }
}

