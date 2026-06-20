using System.Reflection;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CacheableAttributeSpec
{
    // X-cacheable-attribute-init — these types use the ATTRIBUTE named-argument syntax (not object
    // initializers). Named attribute arguments require read-write (set) properties; if any of these setters
    // regresses to init-only, the named-arg form below is a CS0617 compile error and this whole suite fails
    // to BUILD. So the [Fact]s that read them are also the permanent compile-time guard for the fix.
    [Cacheable(60, L1TtlSeconds = 10, SlidingTtlSeconds = 30, AllowStaleForSeconds = 15)]
    private sealed class FullyConfiguredEntity { }

    [CachePolicy(CacheScope.ControllerAction, "k:{Id}",
        Tier = CacheTier.LocalOnly,
        Strategy = CacheStrategy.GetOnly,
        ForceCoherenceBroadcast = false,
        Region = "tenant-a",
        Tags = new[] { "t1" })]
    private sealed class PolicyConfiguredAction { }

    [Fact]
    public void CacheableAttributeSyntax_WithAllIntegerSecondNamedArgs_Compiles_AndMaterializesEveryTimeSpan()
    {
        // The documented usage from CacheableAttribute's own XML doc — uncompilable while the setters were
        // init-only (CS0617). This is the exact bug X-cacheable-attribute-init fixes.
        var attr = typeof(FullyConfiguredEntity).GetCustomAttribute<CacheableAttribute>()!;

        attr.AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(60));
        attr.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(10));
        attr.SlidingTtl.Should().Be(TimeSpan.FromSeconds(30));
        attr.AllowStaleFor.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void CachePolicyAttributeSyntax_WithEnumStringAndArrayNamedArgs_Compiles_AndMaterializes()
    {
        // The same init-only bug hit every named-arg property on the base attribute (enums/strings/bool/array),
        // not just the TTL bridge — the complete fix flips them all to set.
        var attr = typeof(PolicyConfiguredAction).GetCustomAttribute<CachePolicyAttribute>()!;

        attr.Scope.Should().Be(CacheScope.ControllerAction);
        attr.Tier.Should().Be(CacheTier.LocalOnly);
        attr.Strategy.Should().Be(CacheStrategy.GetOnly);
        attr.ForceCoherenceBroadcast.Should().BeFalse();
        attr.Region.Should().Be("tenant-a");
        attr.Tags.Should().BeEquivalentTo(new[] { "t1" });
    }

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
