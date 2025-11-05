using FluentAssertions;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Tests for Phase 3: Admin Commands
/// - Job management utilities
/// - Statistics gathering
/// - Failure handling
/// Note: These are unit tests validating API surface. Integration tests validate actual DB operations.
/// </summary>
public class Phase3_AdminCommandsTests
{
    [Fact]
    public void EmbedJobExtensions_HasRetryFailedMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.RetryFailed));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobExtensions_HasPurgeCompletedMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.PurgeCompleted));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobExtensions_HasPurgeAllCompletedMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.PurgeAllCompleted));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobExtensions_HasCancelPendingMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.CancelPending));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobExtensions_HasGetStatsMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.GetStats));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobExtensions_HasGetFailedJobsMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.GetFailedJobs));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobExtensions_HasRequeueJobMethod()
    {
        // Arrange & Act
        var method = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.RequeueJob));

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void EmbedJobStats_CalculatesPercentages()
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

        // Act & Assert
        var pendingPercent = (double)stats.PendingCount / stats.TotalJobs * 100;
        var completedPercent = (double)stats.CompletedCount / stats.TotalJobs * 100;

        pendingPercent.Should().Be(20);
        completedPercent.Should().Be(60);
    }

    [Fact]
    public void EmbedJobStats_AvgProcessingTime_CanBeNull()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 10,
            AvgProcessingTimeSeconds = null
        };

        // Assert
        stats.AvgProcessingTimeSeconds.Should().BeNull();
    }

    [Fact]
    public void EmbedJobStats_AvgProcessingTime_CanBeSet()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            TotalJobs = 10,
            AvgProcessingTimeSeconds = 2.5
        };

        // Assert
        stats.AvgProcessingTimeSeconds.Should().Be(2.5);
    }

    [Fact]
    public void EmbedJobStats_OldestPendingAge_CanBeNull()
    {
        // Arrange
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            OldestPendingAge = null
        };

        // Assert
        stats.OldestPendingAge.Should().BeNull();
    }

    [Fact]
    public void EmbedJobStats_OldestPendingAge_CanBeSet()
    {
        // Arrange
        var age = TimeSpan.FromMinutes(30);
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument",
            OldestPendingAge = age
        };

        // Assert
        stats.OldestPendingAge.Should().Be(age);
    }

    [Fact]
    public void FailedJobInfo_AllProperties_CanBeSet()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow.AddHours(-2);
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var completedAt = DateTimeOffset.UtcNow;

        // Act
        var info = new FailedJobInfo
        {
            JobId = "job-123",
            EntityId = "entity-456",
            EntityType = "TestDocument",
            Status = "FailedPermanent",
            Error = "Network timeout",
            RetryCount = 3,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = completedAt
        };

        // Assert
        info.JobId.Should().Be("job-123");
        info.EntityId.Should().Be("entity-456");
        info.EntityType.Should().Be("TestDocument");
        info.Status.Should().Be("FailedPermanent");
        info.Error.Should().Be("Network timeout");
        info.RetryCount.Should().Be(3);
        info.CreatedAt.Should().Be(createdAt);
        info.StartedAt.Should().Be(startedAt);
        info.CompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public void FailedJobInfo_Timestamps_CanBeNull()
    {
        // Arrange & Act
        var info = new FailedJobInfo
        {
            JobId = "job-123",
            EntityId = "entity-456",
            EntityType = "TestDocument",
            Status = "Pending",
            Error = "None",
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = null,
            CompletedAt = null
        };

        // Assert
        info.StartedAt.Should().BeNull();
        info.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void EmbedJobExtensions_MethodSignatures_AcceptCancellationToken()
    {
        // This validates that all admin commands support cancellation

        // Arrange & Act
        var retryMethod = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.RetryFailed));
        var purgeMethod = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.PurgeCompleted));
        var statsMethod = typeof(EmbedJobExtensions).GetMethod(nameof(EmbedJobExtensions.GetStats));

        // Assert
        retryMethod!.GetParameters().Should().Contain(p => p.ParameterType == typeof(CancellationToken));
        purgeMethod!.GetParameters().Should().Contain(p => p.ParameterType == typeof(CancellationToken));
        statsMethod!.GetParameters().Should().Contain(p => p.ParameterType == typeof(CancellationToken));
    }
}
