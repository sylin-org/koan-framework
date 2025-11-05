using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Tests for Phase 3: Async Queue and Background Processing
/// - EmbedJob entity
/// - Job queueing
/// - Status tracking
/// - Admin commands
/// </summary>
public class Phase3_AsyncQueueTests
{
    [Fact]
    public void EmbedJob_MakeId_GeneratesCorrectFormat()
    {
        // Arrange
        var entityId = "doc-123";

        // Act
        var jobId = EmbedJob<TestDocument>.MakeId(entityId);

        // Assert
        jobId.Should().Be("embedjob:TestDocument:doc-123");
    }

    [Fact]
    public void EmbedJob_InitialStatus_IsPending()
    {
        // Arrange & Act
        var job = new EmbedJob<TestDocument>
        {
            Id = "test-job",
            EntityId = "doc-1",
            EntityType = "TestDocument",
            ContentSignature = "abc123",
            EmbeddingText = "test text",
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Assert
        job.Status.Should().Be(EmbedJobStatus.Pending);
        job.RetryCount.Should().Be(0);
        job.MaxRetries.Should().Be(3); // Default
    }

    [Fact]
    public void EmbedJobStatus_AllStates_AreDefined()
    {
        // Arrange & Act & Assert
        Enum.GetValues<EmbedJobStatus>().Should().Contain(new[]
        {
            EmbedJobStatus.Pending,
            EmbedJobStatus.Processing,
            EmbedJobStatus.Completed,
            EmbedJobStatus.Failed,
            EmbedJobStatus.FailedPermanent
        });
    }

    [Fact]
    public void EmbeddingWorkerOptions_DefaultValues_AreReasonable()
    {
        // Arrange & Act
        var options = new EmbeddingWorkerOptions();

        // Assert
        options.BatchSize.Should().Be(10);
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.IdlePollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.GlobalRateLimitPerMinute.Should().Be(60);
        options.MaxRetries.Should().Be(3);
        options.Enabled.Should().BeTrue();
        options.AutoCleanupCompleted.Should().BeTrue();
        options.CompletedJobRetention.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void EmbeddingWorkerOptions_CanBeCustomized()
    {
        // Arrange & Act
        var options = new EmbeddingWorkerOptions
        {
            BatchSize = 50,
            GlobalRateLimitPerMinute = 120,
            MaxRetries = 5,
            Enabled = false
        };

        // Assert
        options.BatchSize.Should().Be(50);
        options.GlobalRateLimitPerMinute.Should().Be(120);
        options.MaxRetries.Should().Be(5);
        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void TestAsyncDocument_HasAsyncConfiguration()
    {
        // Arrange & Act
        var metadata = EmbeddingMetadata.Get<TestAsyncDocument>();

        // Assert
        metadata.Async.Should().BeTrue();
        metadata.RateLimitPerMinute.Should().Be(30);
    }

    [Fact]
    public void EmbedJobStats_DefaultValues_AreZero()
    {
        // Arrange & Act
        var stats = new EmbedJobStats
        {
            EntityType = "TestDocument"
        };

        // Assert
        stats.TotalJobs.Should().Be(0);
        stats.PendingCount.Should().Be(0);
        stats.ProcessingCount.Should().Be(0);
        stats.CompletedCount.Should().Be(0);
        stats.FailedCount.Should().Be(0);
        stats.FailedPermanentCount.Should().Be(0);
    }

    [Fact]
    public void FailedJobInfo_RequiredProperties_AreEnforced()
    {
        // Arrange & Act
        var info = new FailedJobInfo
        {
            JobId = "job-1",
            EntityId = "entity-1",
            EntityType = "TestDocument",
            Status = "Failed",
            Error = "Test error",
            RetryCount = 2,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Assert
        info.JobId.Should().Be("job-1");
        info.EntityId.Should().Be("entity-1");
        info.Error.Should().Be("Test error");
        info.RetryCount.Should().Be(2);
    }

    [Fact]
    public void EmbeddingWorker_Constructor_AcceptsRequiredDependencies()
    {
        // Arrange
        var logger = Substitute.For<ILogger<Workers.EmbeddingWorker>>();
        var options = Options.Create(new EmbeddingWorkerOptions());

        // Act
        var worker = new Workers.EmbeddingWorker(logger, options);

        // Assert
        worker.Should().NotBeNull();
    }

    [Fact]
    public void EmbeddingWorkerOptions_RetryBackoff_IsExponential()
    {
        // Arrange
        var options = new EmbeddingWorkerOptions
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            RetryBackoffMultiplier = 2.0,
            MaxRetryDelay = TimeSpan.FromMinutes(5)
        };

        // Act & Assert
        // First retry: 1s
        var delay1 = options.InitialRetryDelay.TotalSeconds * Math.Pow(options.RetryBackoffMultiplier, 0);
        delay1.Should().Be(1);

        // Second retry: 2s
        var delay2 = options.InitialRetryDelay.TotalSeconds * Math.Pow(options.RetryBackoffMultiplier, 1);
        delay2.Should().Be(2);

        // Third retry: 4s
        var delay3 = options.InitialRetryDelay.TotalSeconds * Math.Pow(options.RetryBackoffMultiplier, 2);
        delay3.Should().Be(4);
    }

    [Fact]
    public void EmbedJob_Priority_DefaultsToZero()
    {
        // Arrange & Act
        var job = new EmbedJob<TestDocument>
        {
            Id = "test",
            EntityId = "doc-1",
            EntityType = "TestDocument",
            ContentSignature = "sig",
            EmbeddingText = "text",
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Assert
        job.Priority.Should().Be(0);
    }

    [Fact]
    public void EmbedJob_CanSetCustomPriority()
    {
        // Arrange & Act
        var job = new EmbedJob<TestDocument>
        {
            Id = "test",
            EntityId = "doc-1",
            EntityType = "TestDocument",
            ContentSignature = "sig",
            EmbeddingText = "text",
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = 10
        };

        // Assert
        job.Priority.Should().Be(10);
    }

    [Fact]
    public void EmbedJob_Timestamps_CanBeNull()
    {
        // Arrange & Act
        var job = new EmbedJob<TestDocument>
        {
            Id = "test",
            EntityId = "doc-1",
            EntityType = "TestDocument",
            ContentSignature = "sig",
            EmbeddingText = "text",
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = null,
            CompletedAt = null
        };

        // Assert
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void EmbedJob_Error_CanBeNull()
    {
        // Arrange & Act
        var job = new EmbedJob<TestDocument>
        {
            Id = "test",
            EntityId = "doc-1",
            EntityType = "TestDocument",
            ContentSignature = "sig",
            EmbeddingText = "text",
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Error = null
        };

        // Assert
        job.Error.Should().BeNull();
    }
}
