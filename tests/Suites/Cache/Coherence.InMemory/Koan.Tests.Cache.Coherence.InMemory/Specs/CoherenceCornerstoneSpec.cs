using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Coherence.InMemory.Channel;
using Koan.Cache.Options;
using Koan.Tests.Cache.Coherence.InMemory.Support;

namespace Koan.Tests.Cache.Coherence.InMemory.Specs;

/// <summary>
/// The architectural canary: two LayeredCache instances + InMemoryCoherenceBus prove the
/// full cross-node coherence contract within a single process — no Redis required.
/// </summary>
public sealed class CoherenceCornerstoneSpec
{
    private static readonly CacheKey Key = new("Todo:_:abc-123");
    private static readonly CacheValue Value = CacheValue.FromJson("\"hello\"", typeof(string));
    private static readonly CacheWriteOptions WriteOpts = CacheWriteOptions.Default with { AbsoluteTtl = TimeSpan.FromMinutes(5) };

    private static (TestNode a, TestNode b, FakeCacheStore sharedL2) BuildTwoNodes()
    {
        var bus = new InMemoryCoherenceBus();
        var sharedL2 = new FakeCacheStore("shared-l2", CacheStorePlacement.Remote);
        var nodeA = new TestNode("A", bus, new FakeCacheStore("memory-a", CacheStorePlacement.Local), sharedL2);
        var nodeB = new TestNode("B", bus, new FakeCacheStore("memory-b", CacheStorePlacement.Local), sharedL2);
        return (nodeA, nodeB, sharedL2);
    }

    [Fact]
    public async Task Write_on_A_evicts_L1_on_B_via_coherence()
    {
        var (a, b, l2) = BuildTwoNodes();
        await using var _a = a;
        await using var _b = b;

        await a.Start();
        await b.Start();

        // Warm B's L1 by reading a value originally seeded in shared L2.
        await l2.Set(Key, Value, WriteOpts, CancellationToken.None);
        var initial = await b.Layered.Read(Key, CacheReadOptions.Default, CancellationToken.None);
        initial.Hit.Should().BeTrue();
        ((FakeCacheStore)b.L1).Contains(Key.Value).Should().BeTrue("B's L1 must be backfilled from shared L2");

        // Now A writes a new value → broadcasts EvictKey.
        await a.Write(Key, Value, WriteOpts);

        // Give the in-process bus a tick to deliver.
        await WaitForBusDelivery();

        // B's L1 must be evicted by the received coherence message.
        ((FakeCacheStore)b.L1).Contains(Key.Value).Should().BeFalse(
            "B's L1 must be evicted by the coherence message from A");

        // Shared L2 still holds A's write (writer's L2.Set is the source of truth).
        l2.Contains(Key.Value).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_on_A_evicts_L1_on_B()
    {
        var (a, b, l2) = BuildTwoNodes();
        await using var _a = a;
        await using var _b = b;

        await a.Start();
        await b.Start();

        // Seed L2 and warm B's L1.
        await l2.Set(Key, Value, WriteOpts, CancellationToken.None);
        await b.Layered.Read(Key, CacheReadOptions.Default, CancellationToken.None);
        ((FakeCacheStore)b.L1).Contains(Key.Value).Should().BeTrue();

        await a.Evict(Key);
        await WaitForBusDelivery();

        ((FakeCacheStore)b.L1).Contains(Key.Value).Should().BeFalse();
    }

    [Fact]
    public async Task Origin_filter_prevents_writer_from_evicting_its_own_L1()
    {
        var (a, b, _) = BuildTwoNodes();
        await using var _a = a;
        await using var _b = b;

        await a.Start();
        await b.Start();

        // A writes its own L1.
        await a.Write(Key, Value, WriteOpts);

        await WaitForBusDelivery();

        // A's L1 must STILL contain the entry — origin filter drops A's own broadcast on its own subscription.
        ((FakeCacheStore)a.L1).Contains(Key.Value).Should().BeTrue(
            "writer must NOT evict its own L1 when receiving the message it just published");
    }

    [Fact]
    public async Task Three_nodes_all_see_each_others_writes()
    {
        var bus = new InMemoryCoherenceBus();
        var sharedL2 = new FakeCacheStore("shared", CacheStorePlacement.Remote);

        var nodes = new[]
        {
            new TestNode("A", bus, new FakeCacheStore("la", CacheStorePlacement.Local), sharedL2),
            new TestNode("B", bus, new FakeCacheStore("lb", CacheStorePlacement.Local), sharedL2),
            new TestNode("C", bus, new FakeCacheStore("lc", CacheStorePlacement.Local), sharedL2),
        };

        foreach (var n in nodes) await n.Start();
        try
        {
            await sharedL2.Set(Key, Value, WriteOpts, CancellationToken.None);

            // Warm B and C from shared L2.
            await nodes[1].Layered.Read(Key, CacheReadOptions.Default, CancellationToken.None);
            await nodes[2].Layered.Read(Key, CacheReadOptions.Default, CancellationToken.None);
            ((FakeCacheStore)nodes[1].L1).Contains(Key.Value).Should().BeTrue();
            ((FakeCacheStore)nodes[2].L1).Contains(Key.Value).Should().BeTrue();

            // A writes — broadcasts to B and C.
            await nodes[0].Write(Key, Value, WriteOpts);
            await WaitForBusDelivery();

            ((FakeCacheStore)nodes[1].L1).Contains(Key.Value).Should().BeFalse("B's L1 must be evicted");
            ((FakeCacheStore)nodes[2].L1).Contains(Key.Value).Should().BeFalse("C's L1 must be evicted");
        }
        finally
        {
            foreach (var n in nodes) await n.DisposeAsync();
        }
    }

    [Fact]
    public async Task EvictByTag_broadcast_invalidates_L1_entries_across_nodes()
    {
        var (a, b, _) = BuildTwoNodes();
        await using var _a = a;
        await using var _b = b;

        await a.Start();
        await b.Start();

        var keyA = new CacheKey("Todo:_:a");
        var keyB = new CacheKey("Todo:_:b");
        var taggedOpts = WriteOpts with { Tags = new HashSet<string> { "Todo" }, ForceCoherenceBroadcast = false };

        // Seed both nodes' L1 with the same tagged entries (no broadcast).
        await a.Layered.Write(keyA, Value, taggedOpts, CancellationToken.None);
        await a.Layered.Write(keyB, Value, taggedOpts, CancellationToken.None);
        await b.Layered.Write(keyA, Value, taggedOpts, CancellationToken.None);
        await b.Layered.Write(keyB, Value, taggedOpts, CancellationToken.None);

        // A broadcasts EvictByTag.
        await a.Coordinator.BroadcastEvictByTag(new HashSet<string> { "Todo" }, region: null, CancellationToken.None);
        await WaitForBusDelivery();

        ((FakeCacheStore)b.L1).Contains(keyA.Value).Should().BeFalse();
        ((FakeCacheStore)b.L1).Contains(keyB.Value).Should().BeFalse();
    }

    [Fact]
    public async Task Coordinator_with_no_channels_BroadcastEvict_is_noop()
    {
        // Verifies the inactive path: no channels, BroadcastEvict returns without throwing.
        // Build a node-like scenario but with an isolated bus only this node uses.
        var bus = new InMemoryCoherenceBus();
        var l2 = new FakeCacheStore("l2", CacheStorePlacement.Remote);
        var node = new TestNode("solo", bus, new FakeCacheStore("l1", CacheStorePlacement.Local), l2);
        await using var _node = node;
        await node.Start();

        var act = async () => await node.Coordinator.BroadcastEvict(Key, region: null, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CoherenceMode_Required_without_channel_throws_when_remote_present()
    {
        var bus = new InMemoryCoherenceBus();
        var l2 = new FakeCacheStore("l2", CacheStorePlacement.Remote);
        var options = new CacheOptions { CoherenceMode = CoherenceMode.Required };

        // Manually build a coordinator with NO channels and Remote tier present.
        var topology = new Koan.Cache.Topology.CacheTopology(
            Local: new FakeCacheStore("l1", CacheStorePlacement.Local),
            Remote: l2);
        var layered = new Koan.Cache.Topology.LayeredCache(
            topology,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Koan.Cache.Topology.LayeredCache>.Instance);

        var coord = new Koan.Cache.Coherence.CoherenceCoordinator(
            new Koan.Cache.Coherence.NodeIdProvider(),
            Array.Empty<ICacheCoherenceChannel>(),
            layered,
            new Koan.Cache.Coherence.CursorStore(),
            new StaticOptionsMonitor<CacheOptions>(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Koan.Cache.Coherence.CoherenceCoordinator>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var act = async () => await coord.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CoherenceMode = Required*");
    }

    [Fact]
    public async Task CoherenceMode_Disabled_skips_all_broadcasts()
    {
        var (a, b, _) = BuildTwoNodes();
        // Replace coordinator options with Disabled
        var disabledOpts = new CacheOptions { CoherenceMode = CoherenceMode.Disabled };
        var bus = new InMemoryCoherenceBus();
        var l2 = new FakeCacheStore("l2", CacheStorePlacement.Remote);

        var nodeA = new TestNode("A", bus, new FakeCacheStore("la", CacheStorePlacement.Local), l2, disabledOpts);
        var nodeB = new TestNode("B", bus, new FakeCacheStore("lb", CacheStorePlacement.Local), l2, disabledOpts);
        await using var _ = nodeA;
        await using var __ = nodeB;
        await nodeA.Start();
        await nodeB.Start();

        // Warm B's L1
        await l2.Set(Key, Value, WriteOpts, CancellationToken.None);
        await nodeB.Layered.Read(Key, CacheReadOptions.Default, CancellationToken.None);
        ((FakeCacheStore)nodeB.L1).Contains(Key.Value).Should().BeTrue();

        // A writes — but coherence is Disabled, so B should NOT be evicted.
        await nodeA.Write(Key, Value, WriteOpts);
        await WaitForBusDelivery();

        ((FakeCacheStore)nodeB.L1).Contains(Key.Value).Should().BeTrue(
            "Disabled mode must suppress all broadcasts; B's L1 stays warm");
    }

    private static Task WaitForBusDelivery() => Task.Delay(50);
}
