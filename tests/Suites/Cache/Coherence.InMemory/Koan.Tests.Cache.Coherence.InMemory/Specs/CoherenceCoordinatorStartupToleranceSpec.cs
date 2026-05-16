using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Coherence;
using Koan.Cache.Coherence.InMemory.Channel;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Tests.Cache.Coherence.InMemory.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Coherence.InMemory.Specs;

/// <summary>
/// Startup-tolerance contract for <see cref="CoherenceCoordinator"/>. A channel whose
/// transport is unreachable at boot (e.g., Redis pub/sub when Redis isn't running) must not
/// kill the host. Bug surfaced by ARCH-0079 Phase 3.3: prior to this fix, any failing
/// <see cref="ICacheCoherenceChannel.Subscribe"/> propagated out of <c>StartAsync</c>, which
/// the .NET host treated as a startup-aborting exception.
/// </summary>
/// <remarks>
/// Test scenarios cover:
/// <list type="bullet">
///   <item>One-failing-one-succeeding in AutoDetect → coordinator active.</item>
///   <item>All-failing in AutoDetect → coordinator inactive but host starts.</item>
///   <item>One-failing in Required → throws (strict violation).</item>
///   <item>All-failing in Required → throws.</item>
/// </list>
/// </remarks>
public sealed class CoherenceCoordinatorStartupToleranceSpec
{
    private static CoherenceCoordinator BuildCoordinator(
        ICacheCoherenceChannel[] channels,
        CoherenceMode mode = CoherenceMode.AutoDetect,
        ICacheStore? remote = null)
    {
        var topology = new CacheTopology(
            new FakeCacheStore("local", CacheStorePlacement.Local),
            remote);
        var layered = new LayeredCache(topology, NullLogger<LayeredCache>.Instance);
        var options = new StaticOptionsMonitor<CacheOptions>(new CacheOptions { CoherenceMode = mode });

        return new CoherenceCoordinator(
            new NodeIdProvider(),
            channels,
            layered,
            new CursorStore(),
            options,
            NullLogger<CoherenceCoordinator>.Instance,
            NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task AutoDetect_with_one_failing_and_one_succeeding_channel_activates_coordinator()
    {
        var bus = new InMemoryCoherenceBus();
        var working = new InMemoryCoherenceChannel(bus);
        var broken = new FailingCoherenceChannel("redis-pubsub");

        var coordinator = BuildCoordinator(new ICacheCoherenceChannel[] { working, broken });

        // The fix: this MUST NOT throw. Pre-fix, the broken channel's Subscribe propagates
        // up, aborts StartAsync, and the host crashes.
        var act = async () => await coordinator.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        coordinator.IsActive.Should().BeTrue("at least one channel subscribed successfully");
    }

    [Fact]
    public async Task AutoDetect_with_all_channels_failing_leaves_coordinator_inactive_but_does_not_throw()
    {
        var broken1 = new FailingCoherenceChannel("redis-pubsub");
        var broken2 = new FailingCoherenceChannel("messaging");

        var coordinator = BuildCoordinator(new ICacheCoherenceChannel[] { broken1, broken2 });

        var act = async () => await coordinator.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "AutoDetect is permissive — failed channels degrade gracefully, host startup continues");

        coordinator.IsActive.Should().BeFalse("no channels successfully subscribed");
    }

    [Fact]
    public async Task Required_with_any_channel_failing_throws()
    {
        var bus = new InMemoryCoherenceBus();
        var working = new InMemoryCoherenceChannel(bus);
        var broken = new FailingCoherenceChannel("redis-pubsub");

        var coordinator = BuildCoordinator(
            new ICacheCoherenceChannel[] { working, broken },
            mode: CoherenceMode.Required);

        var act = async () => await coordinator.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "Required mode means every channel must be operational");
    }

    [Fact]
    public async Task Required_with_all_channels_failing_throws()
    {
        var broken1 = new FailingCoherenceChannel("redis-pubsub");
        var broken2 = new FailingCoherenceChannel("messaging");

        var coordinator = BuildCoordinator(
            new ICacheCoherenceChannel[] { broken1, broken2 },
            mode: CoherenceMode.Required);

        var act = async () => await coordinator.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AutoDetect_with_only_one_channel_failing_keeps_host_alive()
    {
        // Edge case: single channel registered and it fails. Coordinator stays inactive
        // (no broadcasts will go anywhere), but host startup must not be blocked.
        var broken = new FailingCoherenceChannel("redis-pubsub");
        var coordinator = BuildCoordinator(new ICacheCoherenceChannel[] { broken });

        var act = async () => await coordinator.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        coordinator.IsActive.Should().BeFalse();
    }
}
