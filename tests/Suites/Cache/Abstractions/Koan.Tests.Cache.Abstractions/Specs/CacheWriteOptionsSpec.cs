using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CacheWriteOptionsSpec
{
    private static CacheWriteOptions Build(TimeSpan? absolute, TimeSpan? l1)
        => CacheWriteOptions.Default with { AbsoluteTtl = absolute, L1AbsoluteTtl = l1 };

    [Fact]
    public void GetEffectiveL1Ttl_WhenL1Set_ReturnsL1Verbatim()
    {
        var opts = Build(absolute: TimeSpan.FromMinutes(5), l1: TimeSpan.FromSeconds(30));

        opts.GetEffectiveL1Ttl().Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GetEffectiveL1Ttl_WhenBothNull_ReturnsNull()
    {
        var opts = Build(absolute: null, l1: null);

        opts.GetEffectiveL1Ttl().Should().BeNull();
    }

    [Fact]
    public void GetEffectiveL1Ttl_WhenL1NullAndAbsoluteLarge_DerivesHalf()
    {
        var opts = Build(absolute: TimeSpan.FromMinutes(10), l1: null);

        opts.GetEffectiveL1Ttl().Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void GetEffectiveL1Ttl_WhenL1NullAndAbsoluteSmall_ClampedToAbsolute()
    {
        // Per ARCH-0075's defense-in-depth invariant "L1 TTL = min(L2, max(30s, L2/2))":
        // Absolute = 20s; inner = max(30s, 10s) = 30s; outer = min(20s, 30s) = 20s.
        // L1 must never outlive L2.
        var opts = Build(absolute: TimeSpan.FromSeconds(20), l1: null);

        opts.GetEffectiveL1Ttl().Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void GetEffectiveL1Ttl_WhenL1NullAndAbsoluteExactly1Minute_DerivesMaxOf30AndHalf()
    {
        // Absolute = 60s; half = 30s; floor = 30s → result = 30s
        var opts = Build(absolute: TimeSpan.FromSeconds(60), l1: null);

        opts.GetEffectiveL1Ttl().Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Default_HasBroadcastEnabledAndEmptyTags()
    {
        var opts = CacheWriteOptions.Default;

        opts.ForceCoherenceBroadcast.Should().BeTrue();
        opts.Tags.Should().BeEmpty();
        opts.AbsoluteTtl.Should().BeNull();
        opts.L1AbsoluteTtl.Should().BeNull();
    }
}
