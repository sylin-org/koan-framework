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
/// <para>
/// <b>Shared transport (ARCH-0080):</b> <see cref="StackExchange.Redis.IConnectionMultiplexer"/>
/// is owned by <c>Koan.Data.Connector.Redis</c> — this adapter consumes it via DI. The data
/// connector reads <c>Koan:Data:Redis:ConnectionString</c> as the single canonical source of
/// truth; this adapter does NOT register its own multiplexer and does NOT read its own
/// connection-string config (the field was removed from <see cref="RedisCacheAdapterOptions"/>).
/// The data connector is guaranteed present via this package's csproj
/// <c>&lt;ProjectReference&gt;</c>; if a consumer explicitly removes the transitive reference,
/// resolving <c>IConnectionMultiplexer</c> at boot will throw a standard DI error pointing at
/// the missing type.
/// </para>
/// <para>
/// What this adapter still owns: cache-specific options (<see cref="RedisCacheAdapterOptions.KeyPrefix"/>,
/// <see cref="RedisCacheAdapterOptions.TagPrefix"/>, <see cref="RedisCacheAdapterOptions.Database"/>,
/// <see cref="RedisCacheAdapterOptions.InstanceName"/>), the broadcast channel name
/// (<c>RedisCacheBroadcastOptions.ChannelName</c>), and the <c>ICacheStore</c> and
/// node-broadcast provider registrations.
/// </para>
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

        // IConnectionMultiplexer is registered by Koan.Data.Connector.Redis (the canonical
        // owner, per ARCH-0080). This adapter does not duplicate that registration; it just
        // injects the multiplexer into RedisCacheStore and RedisCacheCommunicationAdapter via DI.

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
        module.AddNote("IConnectionMultiplexer is owned by Koan.Data.Connector.Redis (per ARCH-0080); this adapter injects it.");
    }
}
