using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Coherence;
using Koan.Cache.Coherence.InMemory.Channel;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Cache.Coherence.InMemory.Support;

/// <summary>
/// A standalone test "node" representing one process. Owns its own L1 + LayeredCache +
/// CoherenceCoordinator, but shares a single <see cref="InMemoryCoherenceBus"/> and (optionally)
/// a single Remote tier with sibling nodes — modelling multi-node deployments in-process.
/// </summary>
internal sealed class TestNode : IAsyncDisposable
{
    public string Name { get; }
    public ICacheStore L1 { get; }
    public ICacheStore? L2 { get; }
    public LayeredCache Layered { get; }
    public CoherenceCoordinator Coordinator { get; }
    public InMemoryCoherenceChannel Channel { get; }

    public TestNode(string name, InMemoryCoherenceBus bus, ICacheStore l1, ICacheStore? l2, CacheOptions? options = null)
    {
        Name = name;
        L1 = l1;
        L2 = l2;
        var topology = new CacheTopology(l1, l2);
        Layered = new LayeredCache(topology, NullLogger<LayeredCache>.Instance);

        var nodeIdProvider = new NodeIdProvider();
        var cursors = new CursorStore();
        Channel = new InMemoryCoherenceChannel(bus);

        var monitor = new StaticOptionsMonitor<CacheOptions>(options ?? new CacheOptions());
        Coordinator = new CoherenceCoordinator(
            nodeIdProvider,
            new[] { (ICacheCoherenceChannel)Channel },
            Layered,
            cursors,
            monitor,
            NullLogger<CoherenceCoordinator>.Instance,
            NullLoggerFactory.Instance);
    }

    public Task Start(CancellationToken ct = default) => Coordinator.StartAsync(ct);
    public Task Stop(CancellationToken ct = default) => Coordinator.StopAsync(ct);

    /// <summary>Convenience: write through the layered path AND broadcast via coordinator (mirrors CacheClient.SetAsync).</summary>
    public async ValueTask Write(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct = default)
    {
        await Layered.Write(key, value, options, ct);
        if (options.ForceCoherenceBroadcast)
            await Coordinator.BroadcastEvict(key, options.Region, ct);
    }

    /// <summary>Convenience: evict through the layered path AND broadcast via coordinator (mirrors CacheClient.Remove).</summary>
    public async ValueTask<bool> Evict(CacheKey key, CancellationToken ct = default)
    {
        var existed = await Layered.Evict(key, ct);
        await Coordinator.BroadcastEvict(key, region: null, ct);
        return existed;
    }

    public ValueTask DisposeAsync()
    {
        Channel.Dispose();
        return Coordinator.DisposeAsync();
    }
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
