using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Core;
using Koan.Tests.Cache.Topology.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class CacheTopologySpec
{
    [ProviderPriority(50)]
    private sealed class HighPriorityLocal : FakeCacheStore
    {
        public HighPriorityLocal() : base("high-priority", CacheStorePlacement.Local) { }
    }

    [ProviderPriority(10)]
    private sealed class LowPriorityLocal : FakeCacheStore
    {
        public LowPriorityLocal() : base("low-priority", CacheStorePlacement.Local) { }
    }

    private static CacheTopology Compile(IEnumerable<ICacheStore> stores, CacheOptions? options = null)
        => new(stores, Options.Create(options ?? new CacheOptions()), NullLogger<CacheTopology>.Instance);

    [Fact]
    public void Empty_store_set_compiles_an_empty_topology()
    {
        var result = Compile([]);
        result.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Local_and_remote_stores_compile_a_layered_topology()
    {
        var result = Compile([
            new FakeCacheStore("memory", CacheStorePlacement.Local),
            new FakeCacheStore("redis", CacheStorePlacement.Remote)
        ]);

        result.IsLayered.Should().BeTrue();
        result.Local!.Name.Should().Be("memory");
        result.Remote!.Name.Should().Be("redis");
        result.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void Provider_priority_decides_once_when_no_pin_is_set()
    {
        var result = Compile([new LowPriorityLocal(), new HighPriorityLocal()]);
        result.Local!.Name.Should().Be("high-priority");
        result.LocalReceipt!.Reason.Should().Be("priority-selection");
    }

    [Fact]
    public void Host_pin_overrides_priority()
    {
        var result = Compile(
            [new HighPriorityLocal(), new LowPriorityLocal()],
            new CacheOptions { LocalProvider = "low-priority" });

        result.Local!.Name.Should().Be("low-priority");
        result.LocalReceipt!.Reason.Should().Be("explicit-binding");
    }

    [Fact]
    public void Missing_explicit_provider_fails_without_weakening_intent()
    {
        var compile = () => Compile(
            [new HighPriorityLocal(), new LowPriorityLocal()],
            new CacheOptions { LocalProvider = "does-not-exist" });

        compile.Should().Throw<InvalidOperationException>()
            .WithMessage("*does-not-exist*Candidates: high-priority, low-priority*");
    }

    [Fact]
    public void Provider_pinned_to_the_wrong_placement_fails_clearly()
    {
        var compile = () => Compile(
            [new FakeCacheStore("redis", CacheStorePlacement.Remote)],
            new CacheOptions { LocalProvider = "redis" });

        compile.Should().Throw<InvalidOperationException>()
            .WithMessage("*redis*Remote*Local provider pin*");
    }

    [Fact]
    public void Equal_priority_uses_stable_identity_not_registration_order()
    {
        var result = Compile([
            new FakeCacheStore("zeta", CacheStorePlacement.Local),
            new FakeCacheStore("alpha", CacheStorePlacement.Local)
        ]);
        result.Local!.Name.Should().Be("alpha");
    }

    [Fact]
    public void Duplicate_provider_identity_is_rejected_by_the_shared_catalog()
    {
        var compile = () => Compile([
            new FakeCacheStore("same", CacheStorePlacement.Local),
            new FakeCacheStore("SAME", CacheStorePlacement.Remote)
        ]);

        compile.Should().Throw<InvalidOperationException>()
            .WithMessage("*identity 'same'*more than once*");
    }

    [Fact]
    public void Explicit_tier_requires_that_tier_to_exist()
    {
        var topology = Compile([new FakeCacheStore("memory", CacheStorePlacement.Local)]);
        var require = () => topology.Require(CacheTier.RemoteOnly, "read");

        require.Should().Throw<InvalidOperationException>()
            .WithMessage("*read*RemoteOnly*Reference a Remote Cache adapter*");
    }
}
