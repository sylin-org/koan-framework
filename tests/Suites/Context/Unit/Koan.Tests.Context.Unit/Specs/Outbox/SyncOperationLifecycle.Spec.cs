using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Koan.Context.Models;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Outbox;

/// <summary>
/// Verifies SyncOperation state transitions that power the transactional outbox flow.
/// </summary>
public class SyncOperationLifecycle_Spec
{
    [Fact]
    public void Create_WithValidInputs_SetsInitialState()
    {
        // Arrange
        var jobId = Guid.NewGuid().ToString();
        var chunkId = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid().ToString();
        var embedding = Enumerable.Range(0, 4).Select(i => (float)i).ToArray();
        var metadata = new { FilePath = "docs/file.md", StartLine = 1, EndLine = 10 };

        // Act
    var indexedFileId = Guid.NewGuid().ToString();

    var operation = SyncOperation.Create(jobId, chunkId, indexedFileId, projectId, embedding, metadata);

        // Assert
        operation.JobId.Should().Be(jobId);
        operation.ChunkId.Should().Be(chunkId);
    operation.IndexedFileId.Should().Be(indexedFileId);
    operation.Id.Should().Be(SyncOperation.ComposeId(chunkId, SyncOperationKind.SyncVector));
    operation.Kind.Should().Be(SyncOperationKind.SyncVector);
        operation.ProjectId.Should().Be(projectId);
        operation.Status.Should().Be(OperationStatus.Pending);
        operation.RetryCount.Should().Be(0);
        operation.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    operation.GetEmbedding().Should().BeEquivalentTo(embedding);
    var metadataResult = operation.GetMetadata<Dictionary<string, object?>>();
    metadataResult.Should().NotBeNull();
    }

    [Fact]
    public void RecordFailure_ExceedsMaxRetries_MovesToDeadLetter()
    {
        // Arrange
        var operation = SyncOperation.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            new[] { 1f, 2f, 3f });

        // Act
        for (var i = 0; i < 5; i++)
        {
            operation.RecordFailure($"failure-{i}");
        }

        // Assert
        operation.Status.Should().Be(OperationStatus.DeadLetter);
        operation.RetryCount.Should().Be(5);
        operation.LastError.Should().Be("failure-4");
        operation.LastAttemptAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarkCompleted_ClearsErrorAndSetsTimestamp()
    {
        // Arrange
        var operation = SyncOperation.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            new[] { 0.1f, 0.2f });
        operation.RecordFailure("transient");

        // Act
        operation.MarkCompleted();

        // Assert
        operation.Status.Should().Be(OperationStatus.Completed);
        operation.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        operation.LastError.Should().BeNull();
    }

    [Fact]
    public void Reset_RequeuesWithDeterministicIdentity()
    {
        // Arrange
        var chunkId = Guid.NewGuid().ToString();
        var originalJob = Guid.NewGuid().ToString();
        var indexedFileId = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid().ToString();
        var operation = SyncOperation.Create(
            originalJob,
            chunkId,
            indexedFileId,
            projectId,
            new[] { 1f, 2f });

        operation.MarkCompleted();

        var newJob = Guid.NewGuid().ToString();
        var newEmbedding = new[] { 5f, 6f };

        // Act
        operation.Reset(newJob, indexedFileId, projectId, newEmbedding);

        // Assert
        operation.Id.Should().Be(SyncOperation.ComposeId(chunkId, SyncOperationKind.SyncVector));
        operation.JobId.Should().Be(newJob);
        operation.Status.Should().Be(OperationStatus.Pending);
        operation.RetryCount.Should().Be(0);
        operation.CompletedAt.Should().BeNull();
        operation.GetEmbedding().Should().BeEquivalentTo(newEmbedding);
    }
}
