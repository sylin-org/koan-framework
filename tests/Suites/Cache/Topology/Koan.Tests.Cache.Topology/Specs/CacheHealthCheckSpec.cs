using System.Reflection;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Coherence;
using Koan.Cache.Diagnostics;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Communication.Signals;
using Koan.Tests.Cache.Topology.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class CacheHealthCheckSpec
{
    private static CacheTopology Compile(params ICacheStore[] stores)
        => new(stores, Options.Create(new CacheOptions()), NullLogger<CacheTopology>.Instance);

    private sealed class ThrowingStore : FakeCacheStore
    {
        public ThrowingStore(CacheStorePlacement placement) : base($"throwing-{placement}", placement) { }

        public new ValueTask Set(Koan.Cache.Abstractions.Primitives.CacheKey key, Koan.Cache.Abstractions.Primitives.CacheValue value, Koan.Cache.Abstractions.Primitives.CacheWriteOptions options, CancellationToken ct)
            => throw new InvalidOperationException("simulated transport failure");
    }

    private static CacheHealthCheck BuildHealthCheck(CacheTopology topology)
    {
        var layered = new LayeredCache(topology, NullLogger<LayeredCache>.Instance);
        var nodeId = new NodeIdProvider();
        var coordinator = new CoherenceCoordinator(
            nodeId,
            layered,
            new FakeSignals(),
            new StaticOptionsMonitor<CacheOptions>(new CacheOptions()),
            NullLogger<CoherenceCoordinator>.Instance);

        coordinator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        var ctor = typeof(CacheHealthCheck).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            new[] { typeof(LayeredCache), typeof(CoherenceCoordinator), typeof(Microsoft.Extensions.Logging.ILogger<CacheHealthCheck>) })!;
        return (CacheHealthCheck)ctor.Invoke(new object[] { layered, coordinator, NullLogger<CacheHealthCheck>.Instance });
    }

    private static readonly HealthCheckContext EmptyContext = new() { Registration = new HealthCheckRegistration("koan-cache", _ => null!, HealthStatus.Unhealthy, null) };

    [Fact]
    public async Task Both_tiers_reachable_returns_Healthy()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var l2 = new FakeCacheStore("redis", CacheStorePlacement.Remote);
        var health = BuildHealthCheck(Compile(l1, l2));

        var result = await health.CheckHealthAsync(EmptyContext, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("topology.local").WhoseValue.Should().Be("memory");
        result.Data.Should().ContainKey("topology.remote").WhoseValue.Should().Be("redis");
    }

    [Fact]
    public async Task LocalOnly_with_only_L1_returns_Healthy()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var health = BuildHealthCheck(Compile(l1));

        var result = await health.CheckHealthAsync(EmptyContext, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["topology.remote"].Should().Be("none");
        result.Data["remote.status"].Should().Be("not-configured");
    }

    [Fact]
    public async Task No_tiers_at_all_returns_Unhealthy()
    {
        var health = BuildHealthCheck(Compile());

        var result = await health.CheckHealthAsync(EmptyContext, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("no tiers");
    }

    [Fact]
    public async Task Health_reports_node_id_and_coherence_state()
    {
        var l1 = new FakeCacheStore("memory", CacheStorePlacement.Local);
        var health = BuildHealthCheck(Compile(l1));

        var result = await health.CheckHealthAsync(EmptyContext, CancellationToken.None);

        result.Data.Should().ContainKey("node.id");
        result.Data.Should().ContainKey("coherence.active");
        result.Data["coherence.active"].Should().Be(false, "a local-only topology needs no peer invalidation");
        result.Data["coherence.provider"].Should().Be("in-process");
    }

    private sealed class FakeSignals : IFrameworkSignalPublisher
    {
        public string ProviderId => "in-process";
        public string Assurance => "process-memory";
        public string BroadcastProviderId => "in-process";
        public string BroadcastAssurance => "process-memory";
        public bool BroadcastIsBuiltIn => true;

        public bool TryPublish<TSignal>(TSignal signal)
            where TSignal : struct, IFrameworkSignal<TSignal> => true;

        public bool TryBroadcast<TSignal>(TSignal signal)
            where TSignal : struct, IFrameworkBroadcast<TSignal> => true;

        public Task Start(CancellationToken ct) => Task.CompletedTask;
        public Task Stop(CancellationToken ct) => Task.CompletedTask;
    }
}
