using Koan.TestPipeline;

namespace Koan.Jobs.Core.Tests.Specs;

public class CronParsingSpec : IClassFixture<JobsCoreTestPipelineFixture>
{
    private readonly JobsCoreTestPipelineFixture _fixture;

    public CronParsingSpec(JobsCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Jobs: Cron parsing works as expected")]
    public async Task CronParsing_WorksAsExpected()
    {
    // Arrange
    var parser = _fixture.GetCronParser();
    var expr = "*/5 * * * *";

    // Act
    var next = parser.GetNextOccurrence(expr, DateTime.UtcNow);

    // Assert
    next.Should().BeAfter(DateTime.UtcNow);
    }
}
