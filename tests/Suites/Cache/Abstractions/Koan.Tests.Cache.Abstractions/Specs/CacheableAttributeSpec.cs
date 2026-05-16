using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CacheableAttributeSpec
{
    [Fact]
    public void DefaultConstructor_AppliesEntityFriendlyDefaults()
    {
        var attr = new CacheableAttribute();

        attr.Scope.Should().Be(CacheScope.Entity);
        attr.KeyTemplate.Should().Be("{TypeName}:{Partition}:{Id}");
        attr.Strategy.Should().Be(CacheStrategy.GetOrSet);
        attr.Tier.Should().Be(CacheTier.Layered);
        attr.AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(300));
        attr.Tags.Should().BeEquivalentTo(new[] { "{TypeName}" });
        attr.ForceCoherenceBroadcast.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithTtlSeconds_SetsAbsoluteTtl()
    {
        var attr = new CacheableAttribute(60);

        attr.AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Constructor_WithTtlSecondsZero_LeavesAbsoluteTtlNull()
    {
        var attr = new CacheableAttribute(0);

        attr.AbsoluteTtl.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNegativeTtl_Throws()
    {
        var act = () => new CacheableAttribute(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void L1TtlSeconds_SetsL1AbsoluteTtl()
    {
        var attr = new CacheableAttribute(300) { L1TtlSeconds = 30 };

        attr.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void L1TtlSecondsZero_LeavesL1Null()
    {
        var attr = new CacheableAttribute(300) { L1TtlSeconds = 0 };

        attr.L1AbsoluteTtl.Should().BeNull();
    }

    [Fact]
    public void L1TtlSecondsNegative_Throws()
    {
        var act = () => new CacheableAttribute(300) { L1TtlSeconds = -1 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SlidingTtlSeconds_SetsSlidingTtl()
    {
        var attr = new CacheableAttribute(300) { SlidingTtlSeconds = 45 };

        attr.SlidingTtl.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void AllowStaleForSeconds_SetsAllowStaleFor()
    {
        var attr = new CacheableAttribute(300) { AllowStaleForSeconds = 10 };

        attr.AllowStaleFor.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Constructor_DefaultStrategy_IsGetOrSet()
    {
        var attr = new CacheableAttribute();

        attr.Strategy.Should().Be(CacheStrategy.GetOrSet);
    }

    [Fact]
    public void InheritsFromCachePolicyAttribute_SoBootstrapperDiscoversIt()
    {
        var attr = new CacheableAttribute();

        attr.Should().BeAssignableTo<CachePolicyAttribute>();
    }

    [Fact]
    public void TypeNameTagToken_IsResolvedSentinel()
    {
        var attr = new CacheableAttribute();

        attr.Tags.Should().Contain(CacheableAttribute.TypeNameTagToken);
    }
}
