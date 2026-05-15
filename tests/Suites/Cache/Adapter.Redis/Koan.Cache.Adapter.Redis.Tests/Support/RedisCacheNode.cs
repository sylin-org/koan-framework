using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Redis.Tests.Support;

/// <summary>
/// A single Koan "cache node" — equivalent to one ASP.NET Core process in a multi-instance
/// deployment. Wires up Memory L1, Redis L2, and the Redis pub/sub coherence channel via the
/// adapter's <c>KoanAutoRegistrar</c>, then starts hosted services (notably
/// <c>CoherenceCoordinator</c>) so the node actually subscribes to the channel.
/// </summary>
/// <remarks>
/// Tests build two nodes pointed at the same Redis container + channel name to simulate a
/// real multi-instance cluster sharing one L2 and one coherence transport.
/// </remarks>
internal sealed class RedisCacheNode : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHostedService[] _hostedServices;

    private RedisCacheNode(ServiceProvider provider, IHostedService[] hostedServices)
    {
        _provider = provider;
        _hostedServices = hostedServices;
    }

    public IServiceProvider Provider => _provider;

    public static async Task<RedisCacheNode> Start(
        string connectionString,
        string keyPrefix,
        string tagPrefix,
        string channelName,
        CancellationToken ct)
    {
        var settings = new Dictionary<string, string?>
        {
            [CacheConstants.Configuration.Redis.Configuration] = connectionString,
            [CacheConstants.Configuration.Redis.KeyPrefix] = keyPrefix,
            [CacheConstants.Configuration.Redis.TagPrefix] = tagPrefix,
            [CacheConstants.Configuration.Redis.ChannelName] = channelName
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Pillar core — Memory L1 + topology + coordinator + client + policies + health.
        services.AddKoanCache(configuration);

        // Reference = Intent: invoke the Redis adapter's auto-registrar manually because we're
        // not running through full Koan host bootstrap in tests. This is the same code path the
        // bootstrapper would execute.
        new Koan.Cache.Adapter.Redis.Initialization.KoanAutoRegistrar().Initialize(services);

        var provider = services.BuildServiceProvider();

        // Start hosted services — most importantly CoherenceCoordinator, which subscribes to
        // the Redis pub/sub channel. Without this, the node is deaf to peer invalidations.
        var hosted = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted)
        {
            await service.StartAsync(ct).ConfigureAwait(false);
        }

        return new RedisCacheNode(provider, hosted);
    }

    public async ValueTask DisposeAsync()
    {
        // Stop hosted services in reverse order; swallow exceptions during teardown.
        foreach (var service in _hostedServices.Reverse())
        {
            try
            {
                await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignored — teardown is best-effort
            }
        }

        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
