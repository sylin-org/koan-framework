using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CachePolicyDescriptorSpec
{
    private static CachePolicyDescriptor Build(
        TimeSpan? absoluteTtl = null,
        TimeSpan? l1Ttl = null,
        bool forceCoherenceBroadcast = true,
        CacheTier tier = CacheTier.Layered)
        => new(
            Scope: CacheScope.Entity,
            KeyTemplate: "{TypeName}:{Partition}:{Id}",
            Strategy: CacheStrategy.GetOrSet,
            Tier: tier,
            AbsoluteTtl: absoluteTtl,
            L1AbsoluteTtl: l1Ttl,
            SlidingTtl: null,
            AllowStaleFor: null,
            Tags: new[] { "Todo" },
            Region: null,
            ScopeId: null,
            ForceCoherenceBroadcast: forceCoherenceBroadcast,
            Metadata: new Dictionary<string, string>(),
            TargetMember: null,
            DeclaringType: typeof(string));

    [Fact]
    public void CarriesAllReshapedFields()
    {
        var d = Build(
            absoluteTtl: TimeSpan.FromMinutes(5),
            l1Ttl: TimeSpan.FromSeconds(30),
            tier: CacheTier.Layered);

        d.Tier.Should().Be(CacheTier.Layered);
        d.L1AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(30));
        d.ForceCoherenceBroadcast.Should().BeTrue();
    }

    [Fact]
    public void ToReadOptions_ProjectsReadSideFieldsOnly()
    {
        var d = Build(absoluteTtl: TimeSpan.FromMinutes(5));

        var ro = d.ToReadOptions();

        ro.Region.Should().BeNull();
        ro.ScopeId.Should().BeNull();
        ro.AllowStaleFor.Should().BeNull();
    }

    [Fact]
    public void ToWriteOptions_CarriesAllTtlsTagsAndBroadcastFlag()
    {
        var d = Build(
            absoluteTtl: TimeSpan.FromMinutes(5),
            l1Ttl: TimeSpan.FromMinutes(1));

        var wo = d.ToWriteOptions();

        wo.AbsoluteTtl.Should().Be(TimeSpan.FromMinutes(5));
        wo.L1AbsoluteTtl.Should().Be(TimeSpan.FromMinutes(1));
        wo.Tags.Should().BeEquivalentTo("Todo");
        wo.ForceCoherenceBroadcast.Should().BeTrue();
    }

    [Fact]
    public void ToWriteOptions_TagsAreOrdinalIgnoreCaseHashSet()
    {
        var d = Build();

        var wo = d.ToWriteOptions();

        wo.Tags.Should().Contain("Todo");
        wo.Tags.Should().Contain("todo", "tags compare ordinal-ignore-case");
    }

}
