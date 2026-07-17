using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

public sealed class SegmentationCompositionSpec
{
    [Fact]
    public void Active_plan_projects_stable_coverage_without_ambient_values()
    {
        var plan = Plan("private-tenant-value");
        var services = new ServiceCollection()
            .AddSingleton(plan)
            .AddSingleton<ISegmentationRealization>(new Receipt(
                new SegmentationRealizationDescriptor("storage", "path-prefix", ["list", "typed.key"])))
            .BuildServiceProvider();
        var builder = new KoanCompositionBuilder();

        SegmentationCompositionFacts.Project(builder, services);
        builder.ApplyTo(out _, out var capabilities, out _, out _, out var facts);

        capabilities!["segmentation:dimensions"].Should().Equal("tenant");
        capabilities["segmentation:storage"].Should().BeEquivalentTo(
            "guarantee.enforced-or-rejected",
            "list",
            "realization.path-prefix",
            "typed.key");
        facts.Should().Contain(fact => fact.Code == Constants.Diagnostics.Codes.SegmentationDimensionsActive);
        facts.Should().Contain(fact =>
            fact.Code == Constants.Diagnostics.Codes.SegmentationRealizationActive
            && fact.Kind == KoanFactKind.Guarantee);
        KoanFactJson.Serialize(new KoanFactEnvelope(Constants.Diagnostics.FactSchemaVersion, 1, "test", DateTimeOffset.UnixEpoch, true, facts))
            .Should().NotContain("private-tenant-value");
    }

    [Fact]
    public void Empty_plan_emits_no_segmentation_claims()
    {
        var services = new ServiceCollection()
            .AddSingleton(SegmentationPlan.Empty)
            .AddSingleton<ISegmentationRealization>(new Receipt(
                new SegmentationRealizationDescriptor("storage", "path-prefix", ["list"])))
            .BuildServiceProvider();
        var builder = new KoanCompositionBuilder();

        SegmentationCompositionFacts.Project(builder, services);
        builder.ApplyTo(out _, out var capabilities, out _, out _, out var facts);

        capabilities.Should().BeNull();
        facts.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_pillar_receipts_reject_instead_of_overstating_a_guarantee()
    {
        var services = new ServiceCollection()
            .AddSingleton(Plan("tenant-a"))
            .AddSingleton<ISegmentationRealization>(new Receipt(
                new SegmentationRealizationDescriptor("cache", "first", ["generic.key"])))
            .AddSingleton<ISegmentationRealization>(new Receipt(
                new SegmentationRealizationDescriptor("cache", "second", ["generic.key"])))
            .BuildServiceProvider();
        var builder = new KoanCompositionBuilder();

        SegmentationCompositionFacts.Project(builder, services);
        builder.ApplyTo(out _, out _, out _, out _, out var facts);

        facts.Should().ContainSingle(fact =>
            fact.Code == Constants.Diagnostics.Codes.SegmentationRealizationRejected
            && fact.State == KoanFactState.Rejected
            && fact.Subject == "segmentation:cache");
        facts.Should().NotContain(fact => fact.Code == Constants.Diagnostics.Codes.SegmentationRealizationActive);
    }

    [Fact]
    public void Evidence_contract_rejects_prose_and_value_shaped_tokens()
    {
        var act = () => new SegmentationRealizationDescriptor(
            "storage",
            "path-prefix",
            ["tenant/acme"]);

        act.Should().Throw<ArgumentException>();
    }

    private static SegmentationPlan Plan(string currentValue)
    {
        var builder = new SegmentationPlanBuilder();
        builder.ForOwner(new SemanticId("spec.owner")).Require(
            "tenant",
            () => SegmentationValue.For(currentValue),
            appliesTo: null,
            "Establish a tenant context.");
        return builder.Build();
    }

    private sealed record Receipt(SegmentationRealizationDescriptor SegmentationRealization)
        : ISegmentationRealization;
}
