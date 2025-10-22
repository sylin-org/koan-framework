using Koan.TestPipeline;

namespace Koan.AI.Core.Tests.Specs;

public class AdapterFallbackSpec : IClassFixture<AICoreTestPipelineFixture>
{
    private readonly AICoreTestPipelineFixture _fixture;

    public AdapterFallbackSpec(AICoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "AI: Adapter fallback works as expected")]
    public async Task AdapterFallback_Works()
    {
    // Arrange
    var client = _fixture.GetAIClientWithFallback();
    var prompt = "Test fallback.";

    // Simulate primary adapter failure
    client.SimulatePrimaryFailure = true;

    // Act
    var result = await client.CompleteAsync(prompt);

    // Assert
    result.Should().NotBeNull();
    result!.Text.Should().Contain("fallback", StringComparison.OrdinalIgnoreCase);
    }
}
