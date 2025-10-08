using Koan.TestPipeline;

namespace Koan.AI.Core.Tests.Specs;

public class StreamingSpec : IClassFixture<AICoreTestPipelineFixture>
{
    private readonly AICoreTestPipelineFixture _fixture;

    public StreamingSpec(AICoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "AI: Streaming works as expected")]
    public async Task Streaming_Works()
    {
        // Arrange
        var client = _fixture.GetAIClient();
        var prompt = "Stream this text.";

        // Act
        var results = new List<string>();
        await foreach (var chunk in client.StreamCompletionAsync(prompt))
        {
            results.Add(chunk);
        }

        // Assert
        results.Should().NotBeEmpty();
        results.JoinString().Should().Contain("Stream");
    }
}
