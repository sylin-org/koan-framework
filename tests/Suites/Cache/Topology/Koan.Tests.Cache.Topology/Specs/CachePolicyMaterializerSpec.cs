using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Policies;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class CachePolicyMaterializerSpec
{
    private sealed class Todo { }
    private sealed class Product { }

    [Fact]
    public void Cacheable_default_materializes_TypeName_tag_and_derives_L1Ttl()
    {
        var attr = new CacheableAttribute();

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        descriptor.Tags.Should().BeEquivalentTo(new[] { "Todo" }, "TypeName sentinel must resolve to declaringType.Name");
        descriptor.AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(300));
        // 300s / 2 = 150s; max(30s, 150s) = 150s
        descriptor.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(150));
        descriptor.Tier.Should().Be(CacheTier.Layered);
        descriptor.Strategy.Should().Be(CacheStrategy.GetOrSet);
        descriptor.ForceCoherenceBroadcast.Should().BeTrue();
    }

    [Fact]
    public void Cacheable_with_explicit_L1Ttl_keeps_override()
    {
        var attr = new CacheableAttribute(60) { L1TtlSeconds = 10 };

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        descriptor.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(10), "explicit L1 override must win over derivation");
    }

    [Fact]
    public void Cacheable_short_TTL_clamps_L1_to_L2_below_floor()
    {
        // L2=20s; half=10s; floor=30s; clamped to L2=20s.
        // For L2 < 60s, defense-in-depth becomes vacuous — L1 == L2.
        var attr = new CacheableAttribute(20);

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        descriptor.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(20), "L1 must never exceed L2");
    }

    [Fact]
    public void Cacheable_zero_TTL_leaves_L1_null()
    {
        var attr = new CacheableAttribute(0);  // 0 = no expiration

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        descriptor.AbsoluteTtl.Should().BeNull();
        descriptor.L1AbsoluteTtl.Should().BeNull();
    }

    [Fact]
    public void CachePolicy_with_explicit_tier_propagates_to_descriptor()
    {
        var attr = new CachePolicyAttribute(CacheScope.Entity, "x:{Id}")
        {
            Tier = CacheTier.LocalOnly,
            ForceCoherenceBroadcast = false,
            Strategy = CacheStrategy.GetOnly,
        };

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Product));

        descriptor.Tier.Should().Be(CacheTier.LocalOnly);
        descriptor.ForceCoherenceBroadcast.Should().BeFalse();
        descriptor.Strategy.Should().Be(CacheStrategy.GetOnly);
    }

    [Fact]
    public void TypeName_token_resolution_is_case_sensitive()
    {
        var attr = new CachePolicyAttribute(CacheScope.Entity, "k:{Id}")
        {
            Tags = new[] { "{TypeName}", "{typename}", "{TYPENAME}", "Other" }
        };

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        // Only the exact {TypeName} token resolves; the other casings pass through as literal tags.
        descriptor.Tags.Should().Contain("Todo");
        descriptor.Tags.Should().Contain("Other");
        // Distinct ordinal-ignore-case dedup means "{typename}" and "{TYPENAME}" collapse to one.
        descriptor.Tags.Should().Contain(t => t.Equals("{typename}", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Multiple_TypeName_tokens_collapse_to_single_TypeName_tag()
    {
        var attr = new CachePolicyAttribute(CacheScope.Entity, "k:{Id}")
        {
            Tags = new[] { "{TypeName}", "{TypeName}" }
        };

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        descriptor.Tags.Should().BeEquivalentTo(new[] { "Todo" });
    }

    [Fact]
    public void Validation_throws_when_L1Ttl_exceeds_L2Ttl()
    {
        // L1=600s, L2=300s → invalid
        var attr = new CacheableAttribute(300) { L1TtlSeconds = 600 };

        var act = () => CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*L1AbsoluteTtl*cannot exceed AbsoluteTtl*");
    }

    [Fact]
    public void Validation_allows_L1Ttl_equal_to_L2Ttl()
    {
        var attr = new CacheableAttribute(300) { L1TtlSeconds = 300 };

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: typeof(Todo));

        descriptor.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public void ResolveL1Ttl_helper_derives_correctly()
    {
        CachePolicyMaterializer.ResolveL1Ttl(TimeSpan.FromMinutes(10), null)
            .Should().Be(TimeSpan.FromMinutes(5), "half of 10min = 5min, above 30s floor");

        CachePolicyMaterializer.ResolveL1Ttl(TimeSpan.FromSeconds(30), null)
            .Should().Be(TimeSpan.FromSeconds(30), "half=15s flooded to 30s, then clamped to L2=30s");

        CachePolicyMaterializer.ResolveL1Ttl(TimeSpan.FromSeconds(20), null)
            .Should().Be(TimeSpan.FromSeconds(20), "below-floor L2 clamps L1 to L2 (no derivation possible)");

        CachePolicyMaterializer.ResolveL1Ttl(null, null)
            .Should().BeNull("no policy when both inputs null");

        CachePolicyMaterializer.ResolveL1Ttl(TimeSpan.FromHours(1), TimeSpan.FromMinutes(2))
            .Should().Be(TimeSpan.FromMinutes(2), "explicit override wins");
    }

    [Fact]
    public void Null_declaringType_resolves_TypeName_to_Unknown()
    {
        var attr = new CacheableAttribute();

        var descriptor = CachePolicyMaterializer.Materialize(attr, member: null, declaringType: null);

        descriptor.Tags.Should().Contain("Unknown");
    }
}
