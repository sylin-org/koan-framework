using Koan.TestPipeline;

namespace Koan.AI.Core.Tests.Specs;

public class UnhealthySourceRoutingSpec : IClassFixture<AICoreTestPipelineFixture>
{
    private readonly AICoreTestPipelineFixture _fixture;

    public UnhealthySourceRoutingSpec(AICoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "AI: Routes around unhealthy sources")]
    public async Task Routes_Around_UnhealthySources()
    {
    // Arrange
    var client = _fixture.GetAIClientWithMultipleSources();
    client.MarkSourceUnhealthy("primary");
    var prompt = "Route around unhealthy";

    // Act
    var result = await client.CompleteAsync(prompt);

    // Assert
    result.Should().NotBeNull();
    result!.Source.Should().NotBe("primary");
    }
}
