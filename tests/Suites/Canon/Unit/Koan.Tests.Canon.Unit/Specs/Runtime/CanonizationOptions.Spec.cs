namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonizationOptionsSpec
{
    private readonly ITestOutputHelper _output;

    public CanonizationOptionsSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Merge_prefers_primary_and_combines_flags()
        => TestPipeline.For<CanonizationOptionsSpec>(_output, nameof(Merge_prefers_primary_and_combines_flags))
            .Act(ctx =>
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

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task WithTag_and_Copy_create_detached_instances()
        => TestPipeline.For<CanonizationOptionsSpec>(_output, nameof(WithTag_and_Copy_create_detached_instances))
            .Act(ctx =>
            {
                var original = CanonizationOptions.Default;

                var tagged = original.WithTag("tracking", "enabled");
                var copy = tagged.Copy();

                original.Tags.Should().BeEmpty();
                tagged.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");
                copy.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");

                copy.Tags["tracking"] = "mutated";
                tagged.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task WithRequestedViews_normalizes_inputs()
        => TestPipeline.For<CanonizationOptionsSpec>(_output, nameof(WithRequestedViews_normalizes_inputs))
            .Act(ctx =>
            {
                var withViews = CanonizationOptions.Default.WithRequestedViews("canonical", "lineage");
                withViews.RequestedViews.Should().BeEquivalentTo(new[] { "canonical", "lineage" });

                var cleared = withViews.WithRequestedViews(Array.Empty<string>());
                cleared.RequestedViews.Should().BeEmpty();

                return ValueTask.CompletedTask;
            })
            .RunAsync();
}
