using System;
using Koan.Jobs.Core.Tests.Support;
using Koan.Jobs.Model;
using Koan.Jobs.Web;

namespace Koan.Jobs.Core.Tests.Specs.Web;

public class JobMapperSpec
{
    [Fact(DisplayName = "JobMapper: ToSummary maps all fields")]
    public void ToSummary_maps_all_fields()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var completed = now.AddMinutes(5);
        var job = new StubJob
        {
            Name = "test-job",
            Status = JobStatus.Completed,
            Progress = 1.0,
            ProgressMessage = "All done",
            CreatedAt = now,
            CompletedAt = completed,
            LastError = null
        };

        // Act
        var dto = JobMapper.ToSummary(job, executionCount: 3);

        // Assert
        dto.Id.Should().Be(job.Id);
        dto.Name.Should().Be("test-job");
        dto.Status.Should().Be("Completed");
        dto.Progress.Should().Be(1.0);
        dto.ProgressMessage.Should().Be("All done");
        dto.CreatedAt.Should().Be(now);
        dto.CompletedAt.Should().Be(completed);
        dto.LastError.Should().BeNull();
        dto.ExecutionCount.Should().Be(3);
    }

    [Fact(DisplayName = "JobMapper: ToSummaries maps collection")]
    public void ToSummaries_maps_collection()
    {
        // Arrange
        var jobs = new[]
        {
            new StubJob { Name = "job-1", Status = JobStatus.Running },
            new StubJob { Name = "job-2", Status = JobStatus.Failed, LastError = "boom" },
            new StubJob { Name = "job-3", Status = JobStatus.Created }
        };

        // Act
        var dtos = JobMapper.ToSummaries(jobs);

        // Assert
        dtos.Should().HaveCount(3);
        dtos[0].Name.Should().Be("job-1");
        dtos[0].Status.Should().Be("Running");
        dtos[1].Name.Should().Be("job-2");
        dtos[1].LastError.Should().Be("boom");
        dtos[2].Name.Should().Be("job-3");
        dtos[2].Status.Should().Be("Created");
    }

    [Fact(DisplayName = "JobMapper: ToSummary defaults executionCount to zero")]
    public void ToSummary_defaults_executionCount_to_zero()
    {
        var job = new StubJob { Name = "simple" };

        var dto = JobMapper.ToSummary(job);

        dto.ExecutionCount.Should().Be(0);
    }
}
