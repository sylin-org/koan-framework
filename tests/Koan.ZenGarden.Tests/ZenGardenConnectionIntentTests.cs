using FluentAssertions;
using Koan.ZenGarden.Core;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenConnectionIntentTests
{
    [Fact]
    public void TryParse_accepts_minimum_shape()
    {
        var parsed = ZenGardenConnectionIntent.TryParse("zen-garden://mongodb", out var intent);

        parsed.Should().BeTrue();
        intent.Should().NotBeNull();
        intent!.Offering.Should().Be("mongodb");
        intent.Instance.Should().BeNull();
        intent.Capabilities.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_accepts_instance_and_capabilities()
    {
        var parsed = ZenGardenConnectionIntent.TryParse(
            "zen-garden://ollama:dev?cap=llama3.2,nomic-embed-text&cap=model:phi4",
            out var intent);

        parsed.Should().BeTrue();
        intent.Should().NotBeNull();
        intent!.Offering.Should().Be("ollama");
        intent.Instance.Should().Be("dev");
        intent.Capabilities.Should().BeEquivalentTo("llama3.2", "nomic-embed-text", "model:phi4");
        intent.ToOfferingSelector().Should().Be("ollama:dev");
    }

    [Fact]
    public void TryParse_rejects_non_zen_garden_values()
    {
        ZenGardenConnectionIntent.TryParse("mongodb://localhost:27017", out var intent).Should().BeFalse();
        intent.Should().BeNull();
    }
}
