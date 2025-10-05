using FluentAssertions;
using Koan.Canon.Domain.Runtime;
using Xunit;

namespace Koan.Canon.Domain.Tests;

public class CanonizationOptionsTests
{
    [Fact]
    public void Merge_ShouldPrioritizePrimaryValues()
    {
        var primary = new CanonizationOptions
        {
            Origin = "primary",
            ForceRebuild = true,
            Tags = new Dictionary<string, string?> { ["env"] = "dev" }
        };

        var fallback = new CanonizationOptions
        {
            Origin = "fallback",
            SkipDistribution = true,
            Tags = new Dictionary<string, string?> { ["region"] = "us", ["env"] = "prod" }
        };

        var merged = CanonizationOptions.Merge(primary, fallback);

        merged.Origin.Should().Be("primary");
        merged.ForceRebuild.Should().BeTrue();
        merged.SkipDistribution.Should().BeTrue();
        merged.Tags.Should().ContainKey("env").WhoseValue.Should().Be("dev");
        merged.Tags.Should().ContainKey("region").WhoseValue.Should().Be("us");
    }

    [Fact]
    public void WithTag_ShouldNotMutateOriginal()
    {
        var options = CanonizationOptions.Default;

        var updated = options.WithTag("scope", "batch");

        updated.Tags.Should().ContainKey("scope");
        options.Tags.Should().NotContainKey("scope");
    }

    [Fact]
    public void WithRequestedViews_ShouldNormalizeEmpty()
    {
        var options = CanonizationOptions.Default.WithRequestedViews();

        options.RequestedViews.Should().NotBeNull();
        options.RequestedViews.Should().BeEmpty();
    }
}
