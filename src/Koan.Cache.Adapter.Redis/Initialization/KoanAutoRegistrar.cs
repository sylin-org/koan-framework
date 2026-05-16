using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Coherence;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
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
/// Redis storage (<see cref="RedisCacheStore"/> as the L2 tier) AND the Redis pub/sub
/// coherence channel (<see cref="RedisCoherenceChannel"/>). The coherence coordinator then
/// activates automatically in <c>CoherenceMode.AutoDetect</c>.
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
/// <see cref="RedisCacheAdapterOptions.InstanceName"/>), the coherence-channel name
/// (<c>RedisCoherenceChannelOptions.ChannelName</c>), and the <c>ICacheStore</c> +
/// <c>ICacheCoherenceChannel</c> registrations.
/// </para>
/// </remarks>
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
        });
        services.PostConfigure<RedisCoherenceChannelOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ChannelName)) opts.ChannelName = "koan-cache";
        });

        // IConnectionMultiplexer is registered by Koan.Data.Connector.Redis (the canonical
        // owner, per ARCH-0080). This adapter does not duplicate that registration; it just
        // injects the multiplexer into RedisCacheStore and RedisCoherenceChannel via DI.

        // Storage: RedisCacheStore as Remote (L2) tier. Two-generic overload keeps the
        // descriptor's ImplementationType distinguishable from the service type — see the
        // matching comment in CacheServiceCollectionExtensions.AddKoanCache.
        services.TryAddSingleton<RedisCacheStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheStore, RedisCacheStore>(sp => sp.GetRequiredService<RedisCacheStore>()));

        // Coherence: RedisCoherenceChannel as ICacheCoherenceChannel.
        services.TryAddSingleton<RedisCoherenceChannel>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheCoherenceChannel, RedisCoherenceChannel>(sp => sp.GetRequiredService<RedisCoherenceChannel>()));
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var channel = Configuration.Read(cfg, CacheConstants.Configuration.Redis.ChannelName, "koan-cache");
        var prefix = Configuration.Read(cfg, CacheConstants.Configuration.Redis.KeyPrefix, "cache:");

        module.AddSetting("CacheStore", "redis (Remote, [ProviderPriority(100)])");
        module.AddSetting("CoherenceChannel", "redis-pubsub ([ProviderPriority(100)])");
        module.AddSetting("ChannelName", channel ?? "koan-cache");
        module.AddSetting("KeyPrefix", prefix ?? "cache:");
        module.AddNote("IConnectionMultiplexer is owned by Koan.Data.Connector.Redis (per ARCH-0080); this adapter injects it.");
    }
}
