using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Topology;
using Koan.Tests.Cache.Topology.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class LayeredCacheSpec
{
    private static readonly CacheKey Key = new("Todo:_:abc-123");
    private static readonly CacheValue Value = CacheValue.FromJson("\"hello\"", typeof(string));
    private static readonly CacheReadOptions ReadOpts = CacheReadOptions.Default;
    private static readonly CacheWriteOptions WriteOpts = CacheWriteOptions.Default with
    {
        AbsoluteTtl = TimeSpan.FromMinutes(5)
    };

    private static LayeredCache BuildLayered(FakeCacheStore? local, FakeCacheStore? remote)
    {
        var topology = new global::Koan.Cache.Topology.CacheTopology(local, remote);
        return new LayeredCache(topology, NullLogger<LayeredCache>.Instance);
    }

    [Fact]
    public async Task Read_L1Hit_returns_without_querying_L2()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        await l1.Set(Key, Value, WriteOpts, CancellationToken.None);

        var layered = BuildLayered(l1, l2);
        var result = await layered.Read(Key, ReadOpts, CancellationToken.None);

        result.Hit.Should().BeTrue();
        l2.FetchCount.Should().Be(0);
    }

    [Fact]
    public async Task Read_L1Miss_L2Hit_backfills_L1()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        await l2.Set(Key, Value, WriteOpts, CancellationToken.None);
        l2.SetCount = 0;  // reset after seed

        var layered = BuildLayered(l1, l2);
        var result = await layered.Read(Key, ReadOpts, CancellationToken.None);

        result.Hit.Should().BeTrue();
        l1.SetCount.Should().Be(1, "L1 should be backfilled from L2");
    }

    [Fact]
    public async Task Read_both_miss_returns_miss()
    {
        var layered = BuildLayered(
            new FakeCacheStore("memory", CacheStorePlacement.Local),
            new FakeCacheStore("remote", CacheStorePlacement.Remote));

        var result = await layered.Read(Key, ReadOpts, CancellationToken.None);

        result.Hit.Should().BeFalse();
    }

    [Fact]
    public async Task Write_writes_to_both_tiers()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        var layered = BuildLayered(l1, l2);

        await layered.Write(Key, Value, WriteOpts, CancellationToken.None);

        l1.Contains(Key.Value).Should().BeTrue();
        l2.Contains(Key.Value).Should().BeTrue();
    }

    [Fact]
    public async Task Evict_removes_from_both_tiers()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        var layered = BuildLayered(l1, l2);

        await layered.Write(Key, Value, WriteOpts, CancellationToken.None);
        await layered.Evict(Key, CancellationToken.None);

        l1.Contains(Key.Value).Should().BeFalse();
        l2.Contains(Key.Value).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyRemoteInvalidation_touches_L1_only_never_L2()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        var layered = BuildLayered(l1, l2);

        await layered.Write(Key, Value, WriteOpts, CancellationToken.None);

        // Pretend a remote node published an evict for this key.
        var msg = CacheInvalidation.EvictKey(Key, originNodeId: Guid.NewGuid());
        await layered.ApplyRemoteInvalidation(msg, CancellationToken.None);

        l1.Contains(Key.Value).Should().BeFalse("L1 must be evicted on receipt of a remote invalidation");
        l2.Contains(Key.Value).Should().BeTrue(
            "L2 is shared and was already evicted by the originating writer — receiver must NOT touch L2");
    }

    [Fact]
    public async Task ApplyRemoteInvalidation_with_no_L1_is_noop()
    {
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        await l2.Set(Key, Value, WriteOpts, CancellationToken.None);

        var layered = BuildLayered(local: null, remote: l2);
        var msg = CacheInvalidation.EvictKey(Key, originNodeId: Guid.NewGuid());

        var act = async () => await layered.ApplyRemoteInvalidation(msg, CancellationToken.None);

        await act.Should().NotThrowAsync();
        l2.Contains(Key.Value).Should().BeTrue();
    }

    [Fact]
    public async Task Read_LocalOnly_topology_works()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        await l1.Set(Key, Value, WriteOpts, CancellationToken.None);

        var layered = BuildLayered(local: l1, remote: null);
        var result = await layered.Read(Key, ReadOpts, CancellationToken.None);

        result.Hit.Should().BeTrue();
    }

    [Fact]
    public async Task Read_RemoteOnly_topology_works_no_backfill()
    {
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        await l2.Set(Key, Value, WriteOpts, CancellationToken.None);

        var layered = BuildLayered(local: null, remote: l2);
        var result = await layered.Read(Key, ReadOpts, CancellationToken.None);

        result.Hit.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_topology_returns_miss()
    {
        var layered = BuildLayered(local: null, remote: null);
        var result = await layered.Read(Key, ReadOpts, CancellationToken.None);

        result.Hit.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyRemoteInvalidation_EvictByTag_clears_L1_tagged_entries()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("remote", CacheStorePlacement.Remote);
        var layered = BuildLayered(l1, l2);

        var keyA = new CacheKey("Todo:_:a");
        var keyB = new CacheKey("Todo:_:b");
        var keyC = new CacheKey("Other:_:c");

        var tagged = WriteOpts with { Tags = new HashSet<string> { "Todo" } };
        var other = WriteOpts with { Tags = new HashSet<string> { "Other" } };

        await layered.Write(keyA, Value, tagged, CancellationToken.None);
        await layered.Write(keyB, Value, tagged, CancellationToken.None);
        await layered.Write(keyC, Value, other, CancellationToken.None);

        var msg = CacheInvalidation.EvictByTag(new HashSet<string> { "Todo" }, originNodeId: Guid.NewGuid());
        await layered.ApplyRemoteInvalidation(msg, CancellationToken.None);

        l1.Contains(keyA.Value).Should().BeFalse();
        l1.Contains(keyB.Value).Should().BeFalse();
        l1.Contains(keyC.Value).Should().BeTrue("other-tagged entries should remain");
    }
}
