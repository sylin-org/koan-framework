using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Cache.Adapter.Redis.Tests.Support;

/// <summary>
/// A single Koan "cache node" — equivalent to one ASP.NET Core process in a multi-instance
/// deployment. Wires up Memory L1, Redis L2, and the layered Redis every-node provider through
/// the real <c>AddKoan()</c> bootstrap path.
/// </summary>
/// <remarks>
/// <para>
/// Rides <see cref="KoanIntegrationHost"/> (the ARCH-0079 canon helper) for host wiring.
/// This is intentionally the same reflective bootstrap path applications use; the cornerstone
/// proves the capability appears from references without test-only registration code.
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
                services.AddKoan();
            })
            .StartAsync(ct)
            .ConfigureAwait(false);

        return new RedisCacheNode(host);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
