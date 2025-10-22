using Koan.TestPipeline;

namespace Koan.Jobs.Core.Tests.Specs;

public class OrchestratedExecutionSpec : IClassFixture<JobsCoreTestPipelineFixture>
{
    private readonly JobsCoreTestPipelineFixture _fixture;

    public OrchestratedExecutionSpec(JobsCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Jobs: Orchestrated job execution works")]
    public async Task OrchestratedJobExecution_Works()
    {
    // Arrange
    var orchestrator = _fixture.GetOrchestrator();
    var job = _fixture.CreateTestJob();

    // Act
    await orchestrator.ExecuteAsync(job);
    var status = await orchestrator.GetJobStatusAsync(job.Id);

    // Assert
    status.Should().Be("Completed");
    }
}
