using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Coherence;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Cache.Adapter.Redis.Infrastructure;
using Koan.Communication.Adapters;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Redis.Initialization;

/// <summary>
/// Reference = Intent: referencing <c>Koan.Cache.Adapter.Redis</c> auto-registers BOTH the
/// Redis storage (<see cref="RedisCacheStore"/> as the L2 tier) and a layered Redis
/// every-node Communication capability (<see cref="RedisCacheCommunicationAdapter"/>).
/// </summary>
/// <remarks>
/// <c>Koan.Redis</c> owns the shared endpoint and host-lifetime connection. This module owns only Cache storage,
/// placement, and layered invalidation mechanics; referencing it does not activate a Data provider.
/// </remarks>
public sealed class RedisCacheModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Options
        services.AddKoanOptions<RedisCacheAdapterOptions>(Constants.Configuration.Section);
        services.AddKoanOptions<RedisCacheBroadcastOptions>(Constants.Configuration.Section);
        services.PostConfigure<RedisCacheAdapterOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.KeyPrefix)) opts.KeyPrefix = Constants.DefaultKeyPrefix;
            if (string.IsNullOrWhiteSpace(opts.TagPrefix)) opts.TagPrefix = Constants.DefaultTagPrefix;
        });
        services.PostConfigure<RedisCacheBroadcastOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ChannelName)) opts.ChannelName = Constants.DefaultChannelName;
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore, RedisCacheStore>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICommunicationAdapter, RedisCacheCommunicationAdapter>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var channel = Configuration.Read(cfg, Constants.Configuration.ChannelName, Constants.DefaultChannelName);
        var prefix = Configuration.Read(cfg, Constants.Configuration.KeyPrefix, Constants.DefaultKeyPrefix);

        module.AddSetting("CacheStore", $"{Constants.ProviderId} (Remote, [ProviderPriority({Constants.ProviderPriority})])");
        module.AddSetting("FrameworkBroadcasts", $"{Constants.BroadcastProviderId} ([ProviderPriority({Constants.ProviderPriority})], active with Redis L2)");
        module.AddSetting("ChannelName", channel ?? Constants.DefaultChannelName);
        module.AddSetting("KeyPrefix", prefix ?? Constants.DefaultKeyPrefix);
        module.AddNote("Redis endpoint discovery and connection lifetime are owned by Sylin.Koan.Redis.");
        module.AddNote("No Data provider is activated by this Cache adapter.");
    }
}
