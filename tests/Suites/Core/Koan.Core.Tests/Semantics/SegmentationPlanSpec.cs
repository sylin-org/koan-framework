using AwesomeAssertions;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;
using Xunit;

namespace Koan.Core.Tests.Semantics;

public sealed class SegmentationPlanSpec
{
    private sealed class TenantEntity;
    private sealed class HostEntity;

    [Fact]
    public void Empty_builder_publishes_the_shared_empty_plan()
    {
        var plan = new SegmentationPlanBuilder().Build();

        plan.Should().BeSameAs(SegmentationPlan.Empty);
        plan.Untyped.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Dimension_order_is_canonical_and_independent_of_contribution_order()
    {
        var forward = Build(("region", "module-b"), ("tenant", "module-a"));
        var reverse = Build(("tenant", "module-a"), ("region", "module-b"));

        forward.Dimensions.Select(static dimension => dimension.Id)
            .Should().Equal("region", "tenant");
        reverse.Dimensions.Select(static dimension => dimension.Id)
            .Should().Equal("region", "tenant");
    }

    [Fact]
    public void Duplicate_dimension_rejects_before_a_plan_is_published()
    {
        var builder = new SegmentationPlanBuilder();
        Add(builder, "module-a", "tenant", () => SegmentationValue.For("a"));
        Add(builder, "module-b", "TENANT", () => SegmentationValue.For("b"));

        var build = builder.Build;

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*dimension 'tenant'*module-a, module-b*One capability must own*");
    }

    [Fact]
    public void Structural_compilation_never_reads_the_ambient_value()
    {
        var reads = 0;
        var builder = new SegmentationPlanBuilder();
        Add(builder, "module-a", "tenant", () =>
        {
            reads++;
            return SegmentationValue.For("secret-tenant");
        });

        var scope = builder.Build().For(typeof(TenantEntity));

        reads.Should().Be(0);
        scope.DimensionIds.Should().Equal("tenant");

        scope.Bind("entity read").Should().ContainSingle()
            .Which.Value.Should().Be("secret-tenant");
        reads.Should().Be(1);
    }

    [Fact]
    public void Concrete_host_and_missing_states_remain_distinct()
    {
        var current = SegmentationValue.For("tenant-a");
        var builder = new SegmentationPlanBuilder();
        Add(builder, "module-a", "tenant", () => current);
        var scope = builder.Build().Untyped;

        scope.Bind("cache read").Should().ContainSingle()
            .Which.Should().Be(new SegmentationBinding("tenant", "tenant-a"));

        current = SegmentationValue.Host;
        scope.Bind("cache read").Should().BeEmpty();

        current = SegmentationValue.Missing;
        var bind = () => scope.Bind("cache read");
        bind.Should().Throw<SegmentationRequiredException>()
            .WithMessage("cache read requires isolation context 'tenant'*Establish context*");
    }

    [Fact]
    public void Type_applicability_is_compiled_once_without_affecting_untyped_operations()
    {
        var checks = 0;
        var builder = new SegmentationPlanBuilder();
        Add(
            builder,
            "module-a",
            "tenant",
            () => SegmentationValue.For("a"),
            type =>
            {
                checks++;
                return type != typeof(HostEntity);
            });
        var plan = builder.Build();

        plan.For(typeof(TenantEntity)).IsEmpty.Should().BeFalse();
        plan.For(typeof(TenantEntity)).IsEmpty.Should().BeFalse();
        plan.For(typeof(HostEntity)).IsEmpty.Should().BeTrue();
        plan.Untyped.IsEmpty.Should().BeFalse();
        checks.Should().Be(2);
    }

    [Fact]
    public void Independent_hosts_do_not_share_dimensions_or_scope_memos()
    {
        var first = Build(("tenant", "module-a"));
        var second = Build(("region", "module-b"));

        first.For(typeof(TenantEntity)).DimensionIds.Should().Equal("tenant");
        second.For(typeof(TenantEntity)).DimensionIds.Should().Equal("region");
    }

    [Fact]
    public void Runtime_value_string_representation_is_redacted()
    {
        SegmentationValue.For("do-not-report").ToString().Should().Be("Concrete(<redacted>)");
    }

    private static SegmentationPlan Build(params (string Id, string Owner)[] dimensions)
    {
        var builder = new SegmentationPlanBuilder();
        foreach (var (id, owner) in dimensions)
            Add(builder, owner, id, () => SegmentationValue.For($"{id}-value"));
        return builder.Build();
    }

    private static void Add(
        SegmentationPlanBuilder builder,
        string owner,
        string id,
        Func<SegmentationValue> read,
        Func<Type, bool>? appliesTo = null) =>
        builder.ForOwner(new SemanticId(owner)).Require(
            id,
            read,
            appliesTo,
            "Establish context before continuing.");
}
