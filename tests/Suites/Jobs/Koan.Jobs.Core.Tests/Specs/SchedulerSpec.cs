using Koan.TestPipeline;

namespace Koan.Jobs.Core.Tests.Specs;

public class SchedulerSpec : IClassFixture<JobsCoreTestPipelineFixture>
{
    private readonly JobsCoreTestPipelineFixture _fixture;

    public SchedulerSpec(JobsCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Jobs: Scheduler schedules jobs correctly")]
    public async Task Scheduler_SchedulesJobsCorrectly()
    {
    // Arrange
    var scheduler = _fixture.GetScheduler();
    var job = _fixture.CreateTestJob();

    // Act
    await scheduler.ScheduleAsync(job);
    var scheduled = await scheduler.GetScheduledJobsAsync();

    // Assert
    scheduled.Should().Contain(j => j.Id == job.Id);
    }
}
