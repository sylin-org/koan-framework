using Koan.TestPipeline;

namespace Koan.Jobs.Core.Tests.Specs;

public class RetryBackoffSpec : IClassFixture<JobsCoreTestPipelineFixture>
{
    private readonly JobsCoreTestPipelineFixture _fixture;

    public RetryBackoffSpec(JobsCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Jobs: Retry and backoff semantics work")]
    public async Task RetryAndBackoff_SemanticsWork()
    {
    // Arrange
    var job = _fixture.CreateFailingJob();
    var scheduler = _fixture.GetScheduler();

    // Act
    await scheduler.ScheduleAsync(job);
    var attempts = await scheduler.GetJobAttemptsAsync(job.Id);

    // Assert
    attempts.Should().BeGreaterThan(1);
    }
}
