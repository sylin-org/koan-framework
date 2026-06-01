using AwesomeAssertions;
using Koan.AI.Contracts.Shared;
using Koan.AI.Models;
using Xunit;

namespace Koan.AI.Models.Tests;

public class ModelEntryTests
{
    [Fact]
    public void ToRef_ReturnsCorrectModelRef()
    {
        var entry = new ModelEntry
        {
            HubId = "BAAI/bge-large-en-v1.5",
            Version = 2
        };

        var modelRef = entry.ToRef();

        modelRef.Id.Should().Be("BAAI/bge-large-en-v1.5");
        modelRef.Version.Should().Be(2);
    }

    [Fact]
    public void NewEntry_HasDefaultValues()
    {
        var entry = new ModelEntry();

        entry.Version.Should().Be(1);
        entry.Quantization.Should().Be(Quantization.None);
        entry.Capabilities.Should().BeEmpty();
        entry.DeployedTo.Should().BeEmpty();
        entry.Tags.Should().BeEmpty();
        entry.HubId.Should().BeEmpty();
        entry.Base.Should().BeNull();
        entry.Lineage.Should().BeNull();
        entry.LocalPath.Should().BeNull();
        entry.LastUsed.Should().BeNull();
        entry.License.Should().BeNull();
        entry.ContextWindow.Should().BeNull();
        entry.EmbeddingDim.Should().BeNull();
    }
}
