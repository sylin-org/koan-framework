using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents a pending dual-store sync operation for the Transactional Outbox Pattern
/// </summary>
/// <remarks>
/// Provides at-least-once delivery guarantees for dual-store (SQLite + Vector) coordination.
///
/// Parallel Processing Model:
/// - Indexer chunks files and creates SyncOperations (producer stream)
/// - VectorSyncWorker processes operations concurrently (consumer stream)
/// - Both streams run in parallel from the first batch
/// - Job completes when: all files chunked AND all operations synced
///
/// Pattern Flow:
/// 1. Chunk.Save() succeeds (SQLite transaction committed)
/// 2. SyncOperation.Create() records pending vector sync (links to Job)
/// 3. VectorSyncWorker polls for pending operations (every 5 seconds)
/// 4. VectorSyncWorker updates Job.VectorsSynced counter after each success
/// 5. Retry with exponential backoff until success or max retries
/// 6. Dead-letter queue for permanent failures
/// 7. Job marked Complete when VectorsSynced == ChunksCreated
///
/// This ensures no data loss even if vector store is temporarily unavailable.
/// </remarks>
public class SyncOperation : Entity<SyncOperation>
{
    /// <summary>
    /// ID of the Chunk entity this operation targets
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// ID of the Job that created this operation
    /// </summary>
    /// <remarks>
    /// Links the operation back to its originating indexing job.
    /// VectorSyncWorker uses this to update job progress (VectorsSynced counter)
    /// and determine when the job is complete.
    /// </remarks>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// Project ID for this chunk (domain GUID, not formatted partition ID)
    /// </summary>
    /// <remarks>
    /// Stores the raw Project.Id GUID (e.g., "019a6584-3075-7076-ae69-4ced4e2799f5").
    /// VectorSyncWorker delegates partition formatting to the active adapter when saving to the vector store.
    ///
    /// Architectural principle: Domain entities store domain identifiers (ProjectId),
    /// infrastructure code handles formatting (partition IDs are implementation details).
    /// </remarks>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Serialized embedding vector (JSON array of floats)
    /// </summary>
    /// <remarks>
    /// Stored as JSON to avoid binary storage issues and enable inspection.
    /// Deserialized as float[] before sending to vector store.
    /// </remarks>
    public string EmbeddingJson { get; set; } = string.Empty;

    /// <summary>
    /// Serialized metadata object (JSON)
    /// </summary>
    /// <remarks>
    /// Contains file path, commit SHA, line numbers, etc.
    /// Provider-specific metadata is extracted during sync.
    /// </remarks>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Current operation status
    /// </summary>
    public OperationStatus Status { get; set; } = OperationStatus.Pending;

    /// <summary>
    /// Number of retry attempts (max 5)
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When this operation was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this operation was last attempted
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// When this operation was completed successfully
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message from last failed attempt
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Creates a new pending sync operation
    /// </summary>
    public static SyncOperation Create(string jobId, string chunkId, string projectId, float[] embedding, object? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId cannot be empty", nameof(jobId));

        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));

        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("ProjectId cannot be empty", nameof(projectId));

        if (embedding == null || embedding.Length == 0)
            throw new ArgumentException("Embedding cannot be null or empty", nameof(embedding));

        return new SyncOperation
        {
            JobId = jobId,
            ChunkId = chunkId,
            ProjectId = projectId,
            EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(embedding),
            MetadataJson = metadata != null
                ? System.Text.Json.JsonSerializer.Serialize(metadata)
                : null,
            Status = OperationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Marks the operation as completed successfully
    /// </summary>
    public void MarkCompleted()
    {
        Status = OperationStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        LastError = null;
    }

    /// <summary>
    /// Records a failed attempt and increments retry count
    /// </summary>
    public void RecordFailure(string errorMessage)
    {
        RetryCount++;
        LastAttemptAt = DateTime.UtcNow;
        LastError = errorMessage;

        if (RetryCount >= 5)
        {
            Status = OperationStatus.DeadLetter;
        }
    }

    /// <summary>
    /// Deserializes the embedding vector
    /// </summary>
    public float[] GetEmbedding()
    {
        return System.Text.Json.JsonSerializer.Deserialize<float[]>(EmbeddingJson)
               ?? throw new InvalidOperationException("Failed to deserialize embedding");
    }

    /// <summary>
    /// Deserializes the metadata object
    /// </summary>
    public T? GetMetadata<T>()
    {
        if (string.IsNullOrWhiteSpace(MetadataJson))
            return default;

        return System.Text.Json.JsonSerializer.Deserialize<T>(MetadataJson);
    }
}

/// <summary>
/// Status of a sync operation
/// </summary>
public enum OperationStatus
{
    /// <summary>Pending execution</summary>
    Pending = 0,

    /// <summary>Successfully completed</summary>
    Completed = 1,

    /// <summary>Failed after max retries (dead-letter queue)</summary>
    DeadLetter = 2
}
