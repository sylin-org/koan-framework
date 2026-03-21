using FluentAssertions;
using Koan.AI.Contracts.Shared;
using Koan.AI.Models;
using Xunit;

namespace Koan.AI.Models.Tests;

public class ModelQueryTests
{
    [Fact]
    public void DefaultQuery_HasMaxResults20()
    {
        var query = new ModelQuery();

        query.MaxResults.Should().Be(20);
    }

    [Fact]
    public void Query_WithFilters_SetsProperties()
    {
        var query = new ModelQuery
        {
            Keywords = "embedding",
            Task = ModelCapability.Embed,
            MinParameters = 100_000_000,
            MaxParameters = 1_000_000_000,
            Format = ModelFormat.GGUF,
            Quantization = Quantization.Q4_K_M,
            License = "Apache-2.0",
            MaxResults = 50
        };

        query.Keywords.Should().Be("embedding");
        query.Task.Should().Be(ModelCapability.Embed);
        query.MinParameters.Should().Be(100_000_000);
        query.MaxParameters.Should().Be(1_000_000_000);
        query.Format.Should().Be(ModelFormat.GGUF);
        query.Quantization.Should().Be(Quantization.Q4_K_M);
        query.License.Should().Be("Apache-2.0");
        query.MaxResults.Should().Be(50);
    }
}
