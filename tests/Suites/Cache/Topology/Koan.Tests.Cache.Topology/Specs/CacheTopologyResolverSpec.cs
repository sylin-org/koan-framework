using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Tests.Cache.Topology.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class CacheTopologyResolverSpec
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

    private static CacheTopologyResolver Resolver()
        => new(NullLogger<CacheTopologyResolver>.Instance);

    [Fact]
    public void Empty_store_set_returns_Empty_topology()
    {
        var result = Resolver().Resolve(Array.Empty<ICacheStore>(), new CacheOptions());

        result.Local.Should().BeNull();
        result.Remote.Should().BeNull();
    }

    [Fact]
    public void Single_Local_store_resolves_as_LocalOnly()
    {
        var stores = new ICacheStore[] { new FakeCacheStore("memory", CacheStorePlacement.Local) };

        var result = Resolver().Resolve(stores, new CacheOptions());

        result.Local!.Name.Should().Be("memory");
        result.Remote.Should().BeNull();
        result.IsLocalOnly.Should().BeTrue();
    }

    [Fact]
    public void Local_and_Remote_stores_resolve_as_Layered()
    {
        var stores = new ICacheStore[]
        {
            new FakeCacheStore("memory", CacheStorePlacement.Local),
            new FakeCacheStore("redis", CacheStorePlacement.Remote)
        };

        var result = Resolver().Resolve(stores, new CacheOptions());

        result.IsLayered.Should().BeTrue();
        result.Local!.Name.Should().Be("memory");
        result.Remote!.Name.Should().Be("redis");
    }

    [Fact]
    public void Config_pin_overrides_priority()
    {
        var stores = new ICacheStore[]
        {
            new HighPriorityLocal(),
            new LowPriorityLocal()
        };

        var options = new CacheOptions { LocalProvider = "low-priority" };
        var result = Resolver().Resolve(stores, options);

        result.Local!.Name.Should().Be("low-priority");
    }

    [Fact]
    public void ProviderPriority_decides_when_no_pin_set()
    {
        var stores = new ICacheStore[]
        {
            new LowPriorityLocal(),
            new HighPriorityLocal()
        };

        var result = Resolver().Resolve(stores, new CacheOptions());

        result.Local!.Name.Should().Be("high-priority");
    }

    [Fact]
    public void Config_pin_to_missing_provider_falls_back_to_priority()
    {
        var stores = new ICacheStore[]
        {
            new HighPriorityLocal(),
            new LowPriorityLocal()
        };

        var options = new CacheOptions { LocalProvider = "does-not-exist" };
        var result = Resolver().Resolve(stores, options);

        // Falls back to priority ranking
        result.Local!.Name.Should().Be("high-priority");
    }
}
