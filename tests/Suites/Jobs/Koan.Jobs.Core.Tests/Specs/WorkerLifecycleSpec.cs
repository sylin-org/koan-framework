using Koan.TestPipeline;

namespace Koan.Jobs.Core.Tests.Specs;

public class WorkerLifecycleSpec : IClassFixture<JobsCoreTestPipelineFixture>
{
    private readonly JobsCoreTestPipelineFixture _fixture;

    public WorkerLifecycleSpec(JobsCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Jobs: Worker lifecycle is managed correctly")]
    public async Task WorkerLifecycle_IsManagedCorrectly()
    {
    // Arrange
    var worker = _fixture.GetWorker();

    // Act
    await worker.StartAsync();
    var isRunning = worker.IsRunning;
    await worker.StopAsync();

    // Assert
    isRunning.Should().BeTrue();
    worker.IsRunning.Should().BeFalse();
    }
}
