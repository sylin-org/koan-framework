using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Cache.Adapter.Redis.Tests.Support;

/// <summary>
/// A single Koan "cache node" — equivalent to one ASP.NET Core process in a multi-instance
/// deployment. Wires up Memory L1, Redis L2, and the Redis pub/sub coherence channel via the
/// adapter's <c>KoanAutoRegistrar</c>, then starts hosted services (notably
/// <c>CoherenceCoordinator</c>) so the node actually subscribes to the channel.
/// </summary>
/// <remarks>
/// <para>
/// Rides <see cref="KoanIntegrationHost"/> (the ARCH-0079 canon helper) for host wiring.
/// Bootstrap is explicit (<c>AddKoanCache</c> + manual <c>KoanAutoRegistrar.Initialize</c>)
/// rather than reflective <c>AddKoan()</c> — this spec exercises cache adapter behavior
/// in isolation; the cross-pillar reflective discovery path is covered separately by
/// <c>CachePillarBootstrapSpec</c>.
/// </para>
/// <para>
/// Tests build two nodes pointed at the same Redis container + channel name to simulate a
/// real multi-instance cluster sharing one L2 and one coherence transport.
/// </para>
/// </remarks>
internal sealed class RedisCacheNode : IAsyncDisposable
{
    private readonly IntegrationHost _host;

    private RedisCacheNode(IntegrationHost host)
    {
        _host = host;
    }

    public IServiceProvider Provider => _host.Services;

    public static async Task<RedisCacheNode> Start(
        string connectionString,
        string keyPrefix,
        string tagPrefix,
        string channelName,
        CancellationToken ct)
    {
        var host = await KoanIntegrationHost.Configure()
            // Per ARCH-0080: IConnectionMultiplexer is owned by Koan.Data.Connector.Redis;
            // configure its canonical key. The cache adapter consumes the multiplexer via DI.
            .WithSetting("Koan:Data:Redis:ConnectionString", connectionString)
            .WithSetting("Koan:Data:Redis:DisableAutoDetection", "true")
            // Cache-specific knobs the cache adapter still owns.
            .WithSetting(CacheConstants.Configuration.Redis.KeyPrefix, keyPrefix)
            .WithSetting(CacheConstants.Configuration.Redis.TagPrefix, tagPrefix)
            .WithSetting(CacheConstants.Configuration.Redis.ChannelName, channelName)
            .ConfigureServices(services =>
            {
                services.AddLogging();
                // Pillar core — Memory L1 + topology + coordinator + client + policies + health.
                services.AddKoanCache();
                // ARCH-0080 ownership: the data connector owns IConnectionMultiplexer.
                new Koan.Data.Connector.Redis.Initialization.KoanAutoRegistrar().Initialize(services);
                // The cache adapter consumes the multiplexer + registers RedisCacheStore +
                // RedisCoherenceChannel. Manual activation here mirrors what reflective
                // discovery does under AddKoan(); the reflective path is covered by
                // CachePillarBootstrapSpec.
                new Koan.Cache.Adapter.Redis.Initialization.KoanAutoRegistrar().Initialize(services);
            })
            .StartAsync(ct)
            .ConfigureAwait(false);

        return new RedisCacheNode(host);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
