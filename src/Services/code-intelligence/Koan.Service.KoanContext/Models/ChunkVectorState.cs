using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Captures the persisted vector payload and synchronization status for a chunk.
/// </summary>
/// <remarks>
/// Replaces the transactional outbox pattern with a snapshot-first contract:
/// the latest embedding and metadata for a chunk are stored in this table and
/// consumed idempotently by <see cref="VectorSyncWorker"/>. Only one record
/// exists per chunk identity, avoiding duplicate dispatch even if indexing is
/// restarted mid-flight.
/// </remarks>
public sealed class ChunkVectorState : Entity<ChunkVectorState>
{
    /// <summary>
    /// Identifier of the chunk whose payload is stored.
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// Owning indexing job identifier.
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// Project identifier (raw GUID) for partition routing.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Manifest identifier for relational provenance.
    /// </summary>
    public string IndexedFileId { get; set; } = string.Empty;

    /// <summary>
    /// Deterministic version hash representing the current chunk contents.
    /// </summary>
    public string ChunkVersion { get; set; } = string.Empty;

    /// <summary>
    /// Latest synchronization state for this payload.
    /// </summary>
    public VectorSyncState State { get; set; } = VectorSyncState.Pending;

    /// <summary>
    /// Number of attempted synchronizations.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// When the payload was first captured.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time the payload record was updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the most recent sync attempt.
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent successful sync.
    /// </summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>
    /// Error message from the last failed attempt (if any).
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Serialized embedding payload.
    /// </summary>
    public string EmbeddingJson { get; set; } = string.Empty;

    /// <summary>
    /// Serialized metadata payload.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Creates a new snapshot for the supplied chunk.
    /// </summary>
    public static ChunkVectorState Create(
        string chunkId,
        string projectId,
        string jobId,
        string indexedFileId,
        string chunkVersion,
        float[] embedding,
        object? metadata)
    {
        if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("ProjectId cannot be empty", nameof(projectId));
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("JobId cannot be empty", nameof(jobId));
        if (string.IsNullOrWhiteSpace(indexedFileId)) throw new ArgumentException("IndexedFileId cannot be empty", nameof(indexedFileId));
        if (string.IsNullOrWhiteSpace(chunkVersion)) throw new ArgumentException("ChunkVersion cannot be empty", nameof(chunkVersion));
        if (embedding is null || embedding.Length == 0) throw new ArgumentException("Embedding cannot be null or empty", nameof(embedding));

        var state = new ChunkVectorState
        {
            Id = chunkId,
            ChunkId = chunkId
        };

        state.Reset(projectId, jobId, indexedFileId, chunkVersion, embedding, metadata);
        state.CreatedAt = DateTime.UtcNow;
        state.UpdatedAt = state.CreatedAt;
        return state;
    }

    /// <summary>
    /// Resets the snapshot with a fresh payload and marks it pending.
    /// </summary>
    public void Reset(
        string projectId,
        string jobId,
        string indexedFileId,
        string chunkVersion,
        float[] embedding,
        object? metadata)
    {
        ProjectId = projectId;
        JobId = jobId;
        IndexedFileId = indexedFileId;
        ChunkVersion = chunkVersion;

        State = VectorSyncState.Pending;
        AttemptCount = 0;
        LastAttemptAt = null;
        SyncedAt = null;
        LastError = null;
        UpdatedAt = DateTime.UtcNow;

        #pragma warning disable IL2026, IL3050
        EmbeddingJson = JsonSerializer.Serialize(embedding);
        MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);
        #pragma warning restore IL2026, IL3050
    }

    /// <summary>
    /// Marks the payload as successfully synchronized.
    /// </summary>
    public void MarkSynced()
    {
        State = VectorSyncState.Synced;
        SyncedAt = DateTime.UtcNow;
        LastAttemptAt = SyncedAt;
        LastError = null;
        UpdatedAt = SyncedAt.Value;
    }

    /// <summary>
    /// Records a failed synchronization attempt.
    /// </summary>
    public void RecordFailure(string errorMessage, int maxAttempts)
    {
        AttemptCount++;
        LastAttemptAt = DateTime.UtcNow;
        LastError = errorMessage;
        State = AttemptCount >= maxAttempts ? VectorSyncState.Failed : VectorSyncState.Pending;
        UpdatedAt = LastAttemptAt.Value;
    }

    /// <summary>
    /// Resets state after a successful retry preparation.
    /// </summary>
    public void PrepareRetry()
    {
        if (State == VectorSyncState.Failed)
        {
            State = VectorSyncState.Pending;
        }

        LastAttemptAt = null;
        SyncedAt = null;
        LastError = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns the embedding payload.
    /// </summary>
    public float[] GetEmbedding()
    {
        #pragma warning disable IL2026, IL3050
        return JsonSerializer.Deserialize<float[]>(EmbeddingJson)
               ?? throw new InvalidOperationException("Failed to deserialize embedding payload");
        #pragma warning restore IL2026, IL3050
    }

    /// <summary>
    /// Returns the strongly typed metadata payload.
    /// </summary>
    public T? GetMetadata<T>()
    {
        if (string.IsNullOrWhiteSpace(MetadataJson))
        {
            return default;
        }

        #pragma warning disable IL2026, IL3050
        return JsonSerializer.Deserialize<T>(MetadataJson);
        #pragma warning restore IL2026, IL3050
    }
}

/// <summary>
/// Represents synchronization status for a chunk vector payload.
/// </summary>
public enum VectorSyncState
{
    /// <summary>Awaiting synchronization.</summary>
    Pending = 0,

    /// <summary>Successfully synchronized to the vector store.</summary>
    Synced = 1,

    /// <summary>Exceeded retry policy and paused for operator intervention.</summary>
    Failed = 2
}
