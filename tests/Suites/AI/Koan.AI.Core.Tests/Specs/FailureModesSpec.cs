using Koan.TestPipeline;

namespace Koan.AI.Core.Tests.Specs;

public class FailureModesSpec : IClassFixture<AICoreTestPipelineFixture>
{
    private readonly AICoreTestPipelineFixture _fixture;

    public FailureModesSpec(AICoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "AI: Handles failure modes gracefully")]
    public async Task Handles_FailureModes_Gracefully()
    {
    // Arrange
    var client = _fixture.GetAIClient();
    var prompt = "Cause error";

    // Act
    Func<Task> act = async () => await client.CompleteAsync(prompt, forceError: true);

    // Assert
    await act.Should().ThrowAsync<Exception>().WithMessage("*error*");
    }
}
