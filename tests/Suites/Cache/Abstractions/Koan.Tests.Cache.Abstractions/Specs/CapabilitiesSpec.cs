using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CapabilitiesSpec
{
    [Fact]
    public void CacheStoreCapabilitiesNone_AllFalse()
    {
        var caps = CacheStoreCapabilities.None;

        caps.SupportsTags.Should().BeFalse();
        caps.SupportsSlidingTtl.Should().BeFalse();
        caps.SupportsStaleWhileRevalidate.Should().BeFalse();
        caps.SupportsBinary.Should().BeFalse();
        caps.SupportsPersistence.Should().BeFalse();
    }

    [Fact]
    public void CoherenceCapabilitiesBestEffort_DeclaresNoGuarantees()
    {
        var caps = CoherenceCapabilities.BestEffort;

        caps.SupportsCatchUp.Should().BeFalse();
        caps.GuaranteesAtLeastOnce.Should().BeFalse();
        caps.PreservesPerKeyOrder.Should().BeFalse();
    }

    [Fact]
    public void CacheStorePlacement_HasExactlyTwoValues()
    {
        var values = Enum.GetValues<CacheStorePlacement>();

        values.Should().BeEquivalentTo(new[] { CacheStorePlacement.Local, CacheStorePlacement.Remote });
    }
}
