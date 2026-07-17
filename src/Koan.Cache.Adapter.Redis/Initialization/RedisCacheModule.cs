using Koan.Cache.Abstractions;
using Koan.Cache.Adapter.Redis.Coherence;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Cache.Abstractions.Extensions;
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
        services.AddKoanOptions<RedisCacheAdapterOptions>(CacheConstants.Configuration.Redis.Section);
        services.AddKoanOptions<RedisCacheBroadcastOptions>(CacheConstants.Configuration.Redis.Section);
        services.PostConfigure<RedisCacheAdapterOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.KeyPrefix)) opts.KeyPrefix = "cache:";
            if (string.IsNullOrWhiteSpace(opts.TagPrefix)) opts.TagPrefix = "cache:tag:";
        });
        services.PostConfigure<RedisCacheBroadcastOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ChannelName)) opts.ChannelName = "koan-cache";
        });

        // IConnectionMultiplexer is registered by Koan.Data.Connector.Redis (the canonical
        // owner, per ARCH-0080). This adapter does not duplicate that registration; it just
        // injects the multiplexer into RedisCacheStore and RedisCacheCommunicationAdapter via DI.

        // Typed registration helpers hide the descriptor shape so the indistinguishable-
        // descriptor bug class can't return through this adapter.
        services.AddCacheStore<RedisCacheStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICommunicationAdapter, RedisCacheCommunicationAdapter>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var channel = Configuration.Read(cfg, CacheConstants.Configuration.Redis.ChannelName, "koan-cache");
        var prefix = Configuration.Read(cfg, CacheConstants.Configuration.Redis.KeyPrefix, "cache:");

        module.AddSetting("CacheStore", "redis (Remote, [ProviderPriority(100)])");
        module.AddSetting("FrameworkBroadcasts", "redis-cache ([ProviderPriority(100)], active with Redis L2)");
        module.AddSetting("ChannelName", channel ?? "koan-cache");
        module.AddSetting("KeyPrefix", prefix ?? "cache:");
        module.AddNote("IConnectionMultiplexer is owned by Koan.Data.Connector.Redis (per ARCH-0080); this adapter injects it.");
    }
}
