namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonizationOptionsSpec
{
    [Fact]
    public void Merge_prefers_primary_and_combines_flags()
    {
        var fallback = CanonizationOptions.Default
            .WithOrigin("fallback")
            .WithTag("region", "us")
            .WithRequestedViews("canonical");
        fallback = fallback with
        {
            StageBehavior = CanonStageBehavior.Immediate,
            SkipDistribution = false,
            ForceRebuild = true,
            CorrelationId = "fallback-corr"
        };

        var primary = CanonizationOptions.Default
            .WithOrigin("primary")
            .WithTag("region", "eu")
            .WithTag("flow", "import")
            .WithStageBehavior(CanonStageBehavior.StageOnly);
        primary = primary with
        {
            SkipDistribution = true,
            ForceRebuild = false,
            CorrelationId = "primary-corr"
        };

        var merged = CanonizationOptions.Merge(primary, fallback);

        merged.Origin.Should().Be("primary");
        merged.CorrelationId.Should().Be("primary-corr");
        merged.StageBehavior.Should().Be(CanonStageBehavior.StageOnly);
        merged.SkipDistribution.Should().BeTrue();
        merged.ForceRebuild.Should().BeTrue();
        merged.RequestedViews.Should().BeEquivalentTo(new[] { "canonical" });
        merged.Tags.Should().ContainKey("flow").WhoseValue.Should().Be("import");
        merged.Tags.Should().ContainKey("region").WhoseValue.Should().Be("eu");

        merged.Tags["region"] = "asia";
        fallback.Tags.Should().ContainKey("region").WhoseValue.Should().Be("us");
    }

    [Fact]
    public void WithTag_and_Copy_create_detached_instances()
    {
        var original = CanonizationOptions.Default;

        var tagged = original.WithTag("tracking", "enabled");
        var copy = tagged.Copy();

        original.Tags.Should().BeEmpty();
        tagged.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");
        copy.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");

        copy.Tags["tracking"] = "mutated";
        tagged.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");
    }

    [Fact]
    public void WithRequestedViews_normalizes_inputs()
    {
        var withViews = CanonizationOptions.Default.WithRequestedViews("canonical", "lineage");
        withViews.RequestedViews.Should().BeEquivalentTo(new[] { "canonical", "lineage" });

        var cleared = withViews.WithRequestedViews([]);
        cleared.RequestedViews.Should().BeEmpty();
    }
}
