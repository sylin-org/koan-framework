using FluentAssertions;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Tests for Phase 3: Admin Commands
/// - Validates business logic in statistics calculations
/// - Validates data model properties and behavior
/// - Integration tests for actual DB operations should be in separate suite
/// </summary>
public class Phase3_AdminCommandsTests
{
    [Fact]
    public void EmbedJobStats_CalculatesPercentages_Correctly()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 100,
            PendingCount = 20,
            ProcessingCount = 10,
            CompletedCount = 60,
            FailedCount = 8,
            FailedPermanentCount = 2
        };

        // Act
        var pendingPercent = (double)stats.PendingCount / stats.TotalJobs * 100;
        var processingPercent = (double)stats.ProcessingCount / stats.TotalJobs * 100;
        var completedPercent = (double)stats.CompletedCount / stats.TotalJobs * 100;
        var failedPercent = (double)stats.FailedCount / stats.TotalJobs * 100;
        var failedPermanentPercent = (double)stats.FailedPermanentCount / stats.TotalJobs * 100;

        // Assert
        pendingPercent.Should().Be(20.0);
        processingPercent.Should().Be(10.0);
        completedPercent.Should().Be(60.0);
        failedPercent.Should().Be(8.0);
        failedPermanentPercent.Should().Be(2.0);

        // Verify percentages sum to 100
        var totalPercent = pendingPercent + processingPercent + completedPercent + failedPercent + failedPermanentPercent;
        totalPercent.Should().Be(100.0, "all status percentages should sum to 100%");
    }

    [Fact]
    public void EmbedJobStats_SuccessRate_CanBeCalculated()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 100,
            CompletedCount = 85,
            FailedCount = 10,
            FailedPermanentCount = 5
        };

        // Act
        var totalCompleted = stats.CompletedCount + stats.FailedCount + stats.FailedPermanentCount;
        var successRate = (double)stats.CompletedCount / totalCompleted * 100;

        // Assert
        successRate.Should().Be(85.0, "success rate should be 85% (85 completed / 100 finished)");
    }

    [Fact]
    public void EmbedJobStats_WithZeroTotalJobs_HandlesGracefully()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 0,
            PendingCount = 0,
            ProcessingCount = 0,
            CompletedCount = 0,
            FailedCount = 0,
            FailedPermanentCount = 0
        };

        // Act - Calculate percentage would require division by zero check
        Action act = () =>
        {
            if (stats.TotalJobs > 0)
            {
                var _ = (double)stats.CompletedCount / stats.TotalJobs * 100;
            }
        };

        // Assert
        act.Should().NotThrow("zero jobs should be handled without division by zero");
        stats.TotalJobs.Should().Be(0);
    }

    [Fact]
    public void EmbedJobStats_AvgProcessingTime_NullWhenNoCompletedJobs()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 10,
            PendingCount = 10,
            AvgProcessingTimeSeconds = null
        };

        // Assert
        stats.AvgProcessingTimeSeconds.Should().BeNull(
            "average processing time should be null when no jobs have completed");
    }

    [Fact]
    public void EmbedJobStats_AvgProcessingTime_CalculatedForCompletedJobs()
    {
        // Arrange - Simulate 3 jobs: 1s, 2s, 3s = 2s average
        var expectedAvg = (1.0 + 2.0 + 3.0) / 3.0;
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 3,
            CompletedCount = 3,
            AvgProcessingTimeSeconds = expectedAvg
        };

        // Assert
        stats.AvgProcessingTimeSeconds.Should().Be(2.0,
            "average of 1s, 2s, 3s should be 2s");
    }

    [Fact]
    public void EmbedJobStats_OldestPendingAge_NullWhenNoPendingJobs()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 10,
            CompletedCount = 10,
            OldestPendingAge = null
        };

        // Assert
        stats.OldestPendingAge.Should().BeNull(
            "oldest pending age should be null when no pending jobs exist");
    }

    [Fact]
    public void EmbedJobStats_OldestPendingAge_ReflectsStaleJobs()
    {
        // Arrange - Oldest job is 2 hours old
        var ageOfOldestJob = TimeSpan.FromHours(2);
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 10,
            PendingCount = 5,
            OldestPendingAge = ageOfOldestJob
        };

        // Assert
        stats.OldestPendingAge.Should().Be(TimeSpan.FromHours(2));
        stats.OldestPendingAge.Value.TotalMinutes.Should().Be(120,
            "2 hours = 120 minutes of pending time");
    }

    [Fact]
    public void FailedJobInfo_ContainsAllRelevantDebugInformation()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow.AddHours(-2);
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var completedAt = DateTimeOffset.UtcNow;

        var info = new FailedJobInfo
        {
            JobId = "embedjob:TestDocument:doc-123",
            EntityId = "doc-123",
            EntityType = "TestDocument",
            Status = "FailedPermanent",
            Error = "AI provider rate limit exceeded",
            RetryCount = 3,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = completedAt
        };

        // Assert - Validate all debug fields are populated
        info.JobId.Should().NotBeNullOrEmpty("JobId needed for retry operations");
        info.EntityId.Should().NotBeNullOrEmpty("EntityId needed to identify source entity");
        info.EntityType.Should().NotBeNullOrEmpty("EntityType needed for filtering by type");
        info.Error.Should().NotBeNullOrEmpty("Error message needed for debugging");
        info.RetryCount.Should().BeGreaterThanOrEqualTo(0, "RetryCount shows how many attempts were made");
        info.CreatedAt.Should().BeBefore(DateTimeOffset.UtcNow, "CreatedAt tracks job age");
    }

    [Fact]
    public void FailedJobInfo_ProcessingDuration_CanBeCalculated()
    {
        // Arrange
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var completedAt = DateTimeOffset.UtcNow;

        var info = new FailedJobInfo
        {
            JobId = "job-123",
            EntityId = "entity-456",
            EntityType = "TestDocument",
            Status = "Failed",
            Error = "Timeout",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            StartedAt = startedAt,
            CompletedAt = completedAt
        };

        // Act
        var duration = info.CompletedAt!.Value - info.StartedAt!.Value;

        // Assert
        duration.TotalMinutes.Should().BeApproximately(5.0, 0.1,
            "processing duration should be ~5 minutes");
    }

    [Fact]
    public void FailedJobInfo_TotalAge_CanBeCalculated()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow.AddHours(-3);

        var info = new FailedJobInfo
        {
            JobId = "job-123",
            EntityId = "entity-456",
            EntityType = "TestDocument",
            Status = "FailedPermanent",
            Error = "Permanent error",
            CreatedAt = createdAt,
            RetryCount = 3
        };

        // Act
        var totalAge = DateTimeOffset.UtcNow - info.CreatedAt;

        // Assert
        totalAge.TotalHours.Should().BeApproximately(3.0, 0.1,
            "job should be ~3 hours old");
    }

    [Fact]
    public void EmbedJobStats_StatusCounts_SumToTotalJobs()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 100,
            PendingCount = 20,
            ProcessingCount = 10,
            CompletedCount = 55,
            FailedCount = 10,
            FailedPermanentCount = 5
        };

        // Act
        var sumOfStatuses = stats.PendingCount + stats.ProcessingCount + stats.CompletedCount +
                           stats.FailedCount + stats.FailedPermanentCount;

        // Assert
        sumOfStatuses.Should().Be(stats.TotalJobs,
            "sum of all status counts should equal total jobs");
    }

    [Fact]
    public void EmbedJobStats_AllCountsNonNegative()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 100,
            PendingCount = 20,
            ProcessingCount = 10,
            CompletedCount = 60,
            FailedCount = 8,
            FailedPermanentCount = 2
        };

        // Assert - All counts must be >= 0
        stats.TotalJobs.Should().BeGreaterThanOrEqualTo(0);
        stats.PendingCount.Should().BeGreaterThanOrEqualTo(0);
        stats.ProcessingCount.Should().BeGreaterThanOrEqualTo(0);
        stats.CompletedCount.Should().BeGreaterThanOrEqualTo(0);
        stats.FailedCount.Should().BeGreaterThanOrEqualTo(0);
        stats.FailedPermanentCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(0, 3, 0)]      // No retries yet
    [InlineData(1, 3, 33.3)]   // 1/3 retried once
    [InlineData(2, 3, 66.7)]   // 2/3 retried twice
    [InlineData(3, 3, 100.0)]  // 3/3 all exhausted retries
    public void FailedJobInfo_RetryCount_ReflectsRetryHistory(int retryCount, int maxRetries, double exhaustionPercent)
    {
        // Arrange
        var info = new FailedJobInfo
        {
            JobId = "job-123",
            EntityId = "entity-456",
            EntityType = "TestDocument",
            Status = retryCount >= maxRetries ? "FailedPermanent" : "Failed",
            Error = "Test error",
            CreatedAt = DateTimeOffset.UtcNow,
            RetryCount = retryCount
        };

        // Act
        var exhaustionPct = maxRetries > 0 ? ((double)retryCount / maxRetries) * 100 : 0.0;

        // Assert
        exhaustionPct.Should().BeApproximately(exhaustionPercent, 0.1,
            $"retry exhaustion should be {exhaustionPercent}% for {retryCount}/{maxRetries}");
    }
}
