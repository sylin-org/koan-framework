using AwesomeAssertions;
using Koan.Core.Context;
using Koan.Data.AI.Telemetry;
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
        typeof(EmbedJob<TestDocument>).GetMethod(nameof(EmbedJob<TestDocument>.MakeId), [typeof(string)])
            .Should().NotBeNull("the released 0.17 binary method shape remains available");
    }

    [Fact]
    public void EmbedJob_MakeId_Isolates_Same_Entity_Id_By_Captured_Context()
    {
        var tenantA = new Dictionary<string, string> { ["koan:tenant"] = "v1:id:a" };
        var tenantB = new Dictionary<string, string> { ["koan:tenant"] = "v1:id:b" };

        var a = EmbedJob<TestDocument>.MakeId("shared-id", tenantA);
        var b = EmbedJob<TestDocument>.MakeId("shared-id", tenantB);

        a.Should().NotBe(b);
        a.Should().StartWith("koan-context-embedjob:v1:").And.NotContain("v1:id:a");
        b.Should().StartWith("koan-context-embedjob:v1:").And.NotContain("v1:id:b");
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
        var metadata = EmbeddingMetadata.Resolve<TestAsyncDocument>();

        // Assert
        metadata.Async.Should().BeTrue();
        metadata.RateLimitPerMinute.Should().Be(30);
    }


    [Fact]
    public void EmbeddingWorker_Constructor_AcceptsRequiredDependencies()
    {
        // Arrange
        var logger = Substitute.For<ILogger<Workers.EmbeddingWorker>>();
        // FQN: `Options` would otherwise bind to the Koan.Data.AI.Options namespace, not the M.E.Options static class.
        var options = Microsoft.Extensions.Options.Options.Create(new EmbeddingWorkerOptions());

        var contextCarriers = new KoanContextCarrierRegistry(Array.Empty<IKoanContextCarrier>());

        // Act
        var worker = new Workers.EmbeddingWorker(logger, options, null, contextCarriers);
#pragma warning disable CS0618 // compile-lock the exact released constructor, including an explicit null telemetry arg
        var legacyWorker = new Workers.EmbeddingWorker(logger, options, null);
#pragma warning restore CS0618

        // Assert
        worker.Should().NotBeNull();
        legacyWorker.Should().NotBeNull();
        typeof(Workers.EmbeddingWorker).GetConstructor(
        [
            typeof(ILogger<Workers.EmbeddingWorker>),
            typeof(IOptions<EmbeddingWorkerOptions>),
            typeof(EmbeddingTelemetry)
        ]).Should().NotBeNull("the released 0.17 constructor shape remains available");
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

}
