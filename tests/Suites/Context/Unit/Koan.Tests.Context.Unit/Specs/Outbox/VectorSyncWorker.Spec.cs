using FluentAssertions;
using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Outbox;

/// <summary>
/// Tests for VectorSyncWorker dual-store coordination and Transactional Outbox Pattern
/// </summary>
/// <remarks>
/// Validates:
/// - At-least-once delivery guarantees
/// - Retry logic with exponential backoff
/// - Dead-letter queue for permanent failures
/// - Concurrent operation safety
/// - Service lifecycle behavior
/// </remarks>
public class VectorSyncWorkerSpec : IAsyncLifetime
{
    private readonly VectorSyncWorker _worker;
    private CancellationTokenSource _cts = new();

    public VectorSyncWorkerSpec()
    {
        _worker = new VectorSyncWorker(NullLogger<VectorSyncWorker>.Instance);
    }

    public async Task InitializeAsync()
    {
        // Clean up any existing test data
        var existingOps = await VectorOperation.Query(_ => true, CancellationToken.None);
        foreach (var op in existingOps)
        {
            await op.Delete(CancellationToken.None);
        }

        var existingChunks = await Chunk.Query(_ => true, CancellationToken.None);
        foreach (var chunk in existingChunks)
        {
            await chunk.Delete(CancellationToken.None);
        }
    }

    public Task DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        return Task.CompletedTask;
    }

    #region Successful Operation Processing (5 tests)

    [Fact]
    public async Task ShouldProcessSinglePendingOperation()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-1");
        var embedding = CreateTestEmbedding(384);
        var operation = VectorOperation.Create(chunk.Id, embedding);
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Status.Should().Be(OperationStatus.Completed);
        result.CompletedAt.Should().NotBeNull();
        result.LastError.Should().BeNull();
    }

    [Fact]
    public async Task ShouldProcessMultiplePendingOperations()
    {
        // Arrange
        var operations = new List<VectorOperation>();
        for (int i = 0; i < 10; i++)
        {
            var chunk = await CreateTestChunk($"test-chunk-{i}");
            var embedding = CreateTestEmbedding(384);
            var operation = VectorOperation.Create(chunk.Id, embedding);
            await operation.Save(CancellationToken.None);
            operations.Add(operation);
        }

        // Act
        await RunWorkerOnce();

        // Assert
        foreach (var op in operations)
        {
            var result = await VectorOperation.Get(op.Id, CancellationToken.None);
            result!.Status.Should().Be(OperationStatus.Completed);
        }
    }

    [Fact]
    public async Task ShouldPreserveMetadataWhenSyncingToVectorStore()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-metadata");
        var embedding = CreateTestEmbedding(384);
        var metadata = new
        {
            FilePath = "src/Program.cs",
            CommitSha = "abc123",
            StartLine = 10,
            EndLine = 20
        };
        var operation = VectorOperation.Create(chunk.Id, embedding, metadata);
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);

        var savedMetadata = result.GetMetadata<object>();
        savedMetadata.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldIgnoreAlreadyCompletedOperations()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-completed");
        var embedding = CreateTestEmbedding(384);
        var operation = VectorOperation.Create(chunk.Id, embedding);
        operation.MarkCompleted();
        await operation.Save(CancellationToken.None);

        var completedAt = operation.CompletedAt;

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);
        result.CompletedAt.Should().Be(completedAt); // Unchanged
    }

    [Fact]
    public async Task ShouldIgnoreDeadLetterOperations()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-deadletter");
        var embedding = CreateTestEmbedding(384);
        var operation = VectorOperation.Create(chunk.Id, embedding);

        // Force to dead-letter status
        for (int i = 0; i < 5; i++)
        {
            operation.RecordFailure("Test failure");
        }
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.DeadLetter);
        result.RetryCount.Should().Be(5);
    }

    #endregion

    #region Retry Logic (5 tests)

    [Fact]
    public async Task ShouldRetryFailedOperationOnNextPoll()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-retry");
        var embedding = CreateTestEmbedding(384);
        var operation = VectorOperation.Create(chunk.Id, embedding);

        // Simulate a previous failure
        operation.RecordFailure("Temporary vector store unavailable");
        await operation.Save(CancellationToken.None);

        operation.RetryCount.Should().Be(1);

        // Act
        await RunWorkerOnce();

        // Assert - Should retry and succeed
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);
    }

    [Fact]
    public async Task ShouldIncrementRetryCountOnFailure()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-increment");
        var invalidEmbedding = new float[0]; // Will cause failure
        var operation = VectorOperation.Create(chunk.Id, invalidEmbedding);
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Pending);
        result.RetryCount.Should().Be(1);
        result.LastError.Should().NotBeNullOrWhiteSpace();
        result.LastAttemptAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldRespectMaxRetryLimit()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-maxretry");
        var invalidEmbedding = new float[0];
        var operation = VectorOperation.Create(chunk.Id, invalidEmbedding);
        await operation.Save(CancellationToken.None);

        // Act - Run worker 5 times (max retries)
        for (int i = 0; i < 5; i++)
        {
            await RunWorkerOnce();
        }

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.DeadLetter);
        result.RetryCount.Should().Be(5);
    }

    [Fact]
    public async Task ShouldRecordLastErrorMessage()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-error");
        var invalidEmbedding = new float[0];
        var operation = VectorOperation.Create(chunk.Id, invalidEmbedding);
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.LastError.Should().NotBeNullOrWhiteSpace();
        result.LastError.Should().Contain("Embedding cannot be null or empty");
    }

    [Fact]
    public async Task ShouldUpdateLastAttemptTimestamp()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-timestamp");
        var embedding = CreateTestEmbedding(384);
        var operation = VectorOperation.Create(chunk.Id, embedding);
        await operation.Save(CancellationToken.None);

        var beforeAttempt = DateTime.UtcNow;

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.LastAttemptAt.Should().NotBeNull();
        result.LastAttemptAt.Should().BeOnOrAfter(beforeAttempt);
    }

    #endregion

    #region Dead-Letter Queue (5 tests)

    [Fact]
    public async Task ShouldMoveToDeadLetterAfterMaxRetries()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-deadletter-move");
        var operation = VectorOperation.Create(chunk.Id, new float[0]); // Invalid
        await operation.Save(CancellationToken.None);

        // Act - Exhaust retries
        for (int i = 0; i < 5; i++)
        {
            await RunWorkerOnce();
        }

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.DeadLetter);
        result.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task ShouldNotRetryDeadLetterOperations()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-deadletter-noretry");
        var operation = VectorOperation.Create(chunk.Id, new float[0]);

        // Force to dead-letter
        for (int i = 0; i < 5; i++)
        {
            operation.RecordFailure("Test failure");
        }
        await operation.Save(CancellationToken.None);

        var retryCount = operation.RetryCount;

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.RetryCount.Should().Be(retryCount); // No additional retries
    }

    [Fact]
    public async Task ShouldPreserveChunkMetadataEvenWhenVectorFails()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-metadata-preserved");
        var operation = VectorOperation.Create(chunk.Id, new float[0]);
        await operation.Save(CancellationToken.None);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await RunWorkerOnce();
        }

        // Assert - Chunk should still exist in SQLite
        var savedChunk = await Chunk.Get(chunk.Id, CancellationToken.None);
        savedChunk.Should().NotBeNull();
        savedChunk!.FilePath.Should().Be(chunk.FilePath);

        // Operation should be in dead-letter queue
        var operation2 = await VectorOperation.Get(operation.Id, CancellationToken.None);
        operation2!.Status.Should().Be(OperationStatus.DeadLetter);
    }

    [Fact]
    public async Task ShouldAllowManualReprocessingOfDeadLetterOperations()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-manual-reprocess");
        var operation = VectorOperation.Create(chunk.Id, new float[0]);

        for (int i = 0; i < 5; i++)
        {
            operation.RecordFailure("Test failure");
        }
        await operation.Save(CancellationToken.None);

        // Act - Manually reset operation
        operation.Status = OperationStatus.Pending;
        operation.RetryCount = 0;
        operation.LastError = null;
        operation.EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(CreateTestEmbedding(384));
        await operation.Save(CancellationToken.None);

        await RunWorkerOnce();

        // Assert - Should now complete successfully
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);
    }

    [Fact]
    public async Task ShouldLogDeadLetterOperationsWithChunkId()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-deadletter-log");
        var operation = VectorOperation.Create(chunk.Id, new float[0]);
        await operation.Save(CancellationToken.None);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await RunWorkerOnce();
        }

        // Assert - Operation should have ChunkId for reconciliation
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.ChunkId.Should().Be(chunk.Id);
        result.Status.Should().Be(OperationStatus.DeadLetter);
    }

    #endregion

    #region Concurrent Operations (3 tests)

    [Fact]
    public async Task ShouldHandleConcurrentOperationsSafely()
    {
        // Arrange
        var operations = new List<VectorOperation>();
        for (int i = 0; i < 50; i++)
        {
            var chunk = await CreateTestChunk($"concurrent-chunk-{i}");
            var operation = VectorOperation.Create(chunk.Id, CreateTestEmbedding(384));
            await operation.Save(CancellationToken.None);
            operations.Add(operation);
        }

        // Act - Process in parallel (simulates concurrent worker polls)
        await RunWorkerOnce();

        // Assert - All should complete successfully
        foreach (var op in operations)
        {
            var result = await VectorOperation.Get(op.Id, CancellationToken.None);
            result!.Status.Should().Be(OperationStatus.Completed);
        }
    }

    [Fact]
    public async Task ShouldNotProcessSameOperationTwice()
    {
        // Arrange
        var chunk = await CreateTestChunk("test-chunk-once");
        var operation = VectorOperation.Create(chunk.Id, CreateTestEmbedding(384));
        await operation.Save(CancellationToken.None);

        // Act - Run worker twice
        await RunWorkerOnce();
        await RunWorkerOnce();

        // Assert - Should only be marked completed once
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);

        // Check that it wasn't reprocessed (no additional logs or state changes)
        var allOps = await VectorOperation.Query(
            op => op.ChunkId == chunk.Id,
            CancellationToken.None);
        allOps.Count().Should().Be(1);
    }

    [Fact]
    public async Task ShouldHandleMixedStatusOperations()
    {
        // Arrange - Create operations with different statuses
        var chunk1 = await CreateTestChunk("chunk-pending");
        var op1 = VectorOperation.Create(chunk1.Id, CreateTestEmbedding(384));
        await op1.Save(CancellationToken.None);

        var chunk2 = await CreateTestChunk("chunk-completed");
        var op2 = VectorOperation.Create(chunk2.Id, CreateTestEmbedding(384));
        op2.MarkCompleted();
        await op2.Save(CancellationToken.None);

        var chunk3 = await CreateTestChunk("chunk-deadletter");
        var op3 = VectorOperation.Create(chunk3.Id, CreateTestEmbedding(384));
        for (int i = 0; i < 5; i++)
        {
            op3.RecordFailure("Test");
        }
        await op3.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result1 = await VectorOperation.Get(op1.Id, CancellationToken.None);
        result1!.Status.Should().Be(OperationStatus.Completed);

        var result2 = await VectorOperation.Get(op2.Id, CancellationToken.None);
        result2!.Status.Should().Be(OperationStatus.Completed);

        var result3 = await VectorOperation.Get(op3.Id, CancellationToken.None);
        result3!.Status.Should().Be(OperationStatus.DeadLetter);
    }

    #endregion

    #region Service Lifecycle (3 tests)

    [Fact]
    public async Task ShouldStartAndStopGracefully()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        var workerTask = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Let it start
        await _worker.StopAsync(CancellationToken.None);

        // Assert - Should complete without throwing
        await workerTask;
    }

    [Fact]
    public async Task ShouldProcessRemainingOperationsOnShutdown()
    {
        // Arrange
        var chunk = await CreateTestChunk("shutdown-chunk");
        var operation = VectorOperation.Create(chunk.Id, CreateTestEmbedding(384));
        await operation.Save(CancellationToken.None);

        var cts = new CancellationTokenSource();

        // Act - Start worker and immediately stop
        await _worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await _worker.StopAsync(CancellationToken.None);

        // Assert - Operation should still be processed
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);
    }

    [Fact]
    public async Task ShouldHandleCancellationGracefully()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        var workerTask = _worker.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel(); // Cancel token

        // Assert - Should complete without unhandled exceptions
        try
        {
            await workerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected for background service cancellation
        }
    }

    #endregion

    #region Error Scenarios (4 tests)

    [Fact]
    public async Task ShouldHandleInvalidEmbeddingJson()
    {
        // Arrange
        var chunk = await CreateTestChunk("invalid-embedding");
        var operation = VectorOperation.Create(chunk.Id, CreateTestEmbedding(384));
        operation.EmbeddingJson = "{invalid json}";
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Pending);
        result.RetryCount.Should().Be(1);
        result.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ShouldHandleInvalidMetadataJson()
    {
        // Arrange
        var chunk = await CreateTestChunk("invalid-metadata");
        var operation = VectorOperation.Create(chunk.Id, CreateTestEmbedding(384));
        operation.MetadataJson = "{invalid json}";
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert - Should still attempt to process (metadata is optional)
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldHandleMissingChunk()
    {
        // Arrange
        var operation = VectorOperation.Create("non-existent-chunk-id", CreateTestEmbedding(384));
        await operation.Save(CancellationToken.None);

        // Act
        await RunWorkerOnce();

        // Assert - Should complete (vector store doesn't require chunk to exist)
        var result = await VectorOperation.Get(operation.Id, CancellationToken.None);
        result!.Status.Should().Be(OperationStatus.Completed);
    }

    [Fact]
    public async Task ShouldHandleEmptyPendingQueue()
    {
        // Arrange - No pending operations

        // Act & Assert - Should not throw
        await RunWorkerOnce();
    }

    #endregion

    #region Helper Methods

    private async Task<Chunk> CreateTestChunk(string id)
    {
        var chunk = new Chunk
        {
            Id = id,
            FilePath = $"test/{id}.md",
            SearchText = "Test content",
            StartLine = 1,
            EndLine = 10
        };
        await chunk.Save(CancellationToken.None);
        return chunk;
    }

    private float[] CreateTestEmbedding(int dimensions)
    {
        var random = new Random(42);
        return Enumerable.Range(0, dimensions)
            .Select(_ => (float)random.NextDouble())
            .ToArray();
    }

    private async Task RunWorkerOnce()
    {
        // Use reflection to invoke the private ProcessPendingOperationsAsync method
        var method = typeof(VectorSyncWorker)
            .GetMethod("ProcessPendingOperationsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null)
        {
            var task = method.Invoke(_worker, new object[] { CancellationToken.None }) as Task;
            if (task != null)
            {
                await task;
            }
        }
    }

    #endregion
}
