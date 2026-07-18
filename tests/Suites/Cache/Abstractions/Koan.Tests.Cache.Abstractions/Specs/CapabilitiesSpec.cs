using Koan.Cache.Abstractions.Capabilities;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Capabilities;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CapabilitiesSpec
{
    [Fact]
    public void CacheCapabilities_AreDeclaredThroughTheSharedCapabilityModel()
    {
        var caps = CacheCaps.Describe(new DescribingStore(), "test-cache");

        caps.Owner.Should().Be("test-cache");
        caps.Has(CacheCaps.Tags).Should().BeTrue();
        caps.Has(CacheCaps.SlidingExpiration).Should().BeTrue();
        caps.Has(CacheCaps.Persistent).Should().BeFalse();
    }

    [Fact]
    public void CacheStorePlacement_HasExactlyTwoValues()
    {
        var values = Enum.GetValues<CacheStorePlacement>();

        values.Should().BeEquivalentTo(new[] { CacheStorePlacement.Local, CacheStorePlacement.Remote });
    }

    private sealed class DescribingStore : IDescribesCapabilities
    {
        public void Describe(ICapabilities caps)
            => caps.Add(CacheCaps.Tags).Add(CacheCaps.SlidingExpiration);
    }
}
