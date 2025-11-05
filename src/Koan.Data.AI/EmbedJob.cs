using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AI;

/// <summary>
/// Represents a queued embedding generation job for async processing.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 3).
/// </summary>
/// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
[Storage(Name = "EmbedJobs")]
public class EmbedJob<TEntity> : Entity<EmbedJob<TEntity>>
    where TEntity : class, IEntity<string>
{
    /// <summary>
    /// Entity ID of the item to embed
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// Entity type name (for diagnostics and filtering)
    /// </summary>
    public required string EntityType { get; set; }

    /// <summary>
    /// Content signature (SHA256 hash) of the entity content at time of queueing
    /// </summary>
    public required string ContentSignature { get; set; }

    /// <summary>
    /// Embedding text computed at time of queueing
    /// </summary>
    public required string EmbeddingText { get; set; }

    /// <summary>
    /// Job status: Pending, Processing, Completed, Failed
    /// </summary>
    public required EmbedJobStatus Status { get; set; }

    /// <summary>
    /// Number of retry attempts (0 for first attempt)
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retry attempts before marking as permanently failed
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// When job was created/queued
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When job started processing (null if not yet started)
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When job completed or failed (null if not finished)
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Model to use for embedding generation (optional, uses default if null)
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Priority for job processing (higher = more urgent)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Creates a unique ID for an embed job based on entity ID.
    /// Format: "embedjob:{EntityType}:{EntityId}"
    /// </summary>
    public static string MakeId(string entityId)
    {
        var entityType = typeof(TEntity).Name;
        return $"embedjob:{entityType}:{entityId}";
    }
}

/// <summary>
/// Status of an embedding job
/// </summary>
public enum EmbedJobStatus
{
    /// <summary>
    /// Job is queued and waiting to be processed
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Job is currently being processed by a worker
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Job completed successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Job failed (may be retried)
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Job failed permanently after max retries
    /// </summary>
    FailedPermanent = 4
}
