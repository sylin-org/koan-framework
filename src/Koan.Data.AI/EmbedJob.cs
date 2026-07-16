using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Core.Context;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AI;

/// <summary>
/// Represents a queued embedding generation job for async processing.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 3).
/// </summary>
/// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
[Storage(Name = "EmbedJobs")]
public class EmbedJob<TEntity> : Entity<EmbedJob<TEntity>>, IAmbientExempt
    where TEntity : class, IEntity<string>
{
    /// <summary>
    /// Entity ID of the item to embed
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The Koan context (tenant, access subject, …) captured at enqueue, keyed by axis. The embedding worker is a
    /// global background service with no context of its own, so it restores this before loading the
    /// entity + writing the vector/state — without it, a <c>[AccessScoped]</c> / tenant-scoped entity reads back as
    /// "not found" (fail-closed) and its embedding never lands. Null when no cross-cutting axis was in scope.
    /// Carried opaquely (this record names no axis), mirroring <c>JobRecord.AmbientCarrier</c>.
    /// The property name is retained for persisted-wire compatibility.
    /// </summary>
    public Dictionary<string, string>? AmbientCarrier { get; set; }

    /// <summary>
    /// Content signature (SHA256 hash) of the entity content at time of queueing
    /// </summary>
    public required string ContentSignature { get; set; }

    /// <summary>
    /// Job status: Pending, Processing, Completed, Failed
    /// </summary>
    public required EmbedJobStatus Status { get; set; }

    /// <summary>
    /// Number of retry attempts (0 for first attempt)
    /// </summary>
    public int RetryCount { get; set; }

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
    /// Creates the durable queue identity for one Entity in one captured Koan context. The unscoped form retains the
    /// original <c>embedjob:{EntityType}:{EntityId}</c> shape; a scoped form uses a value-opaque context fingerprint so
    /// equal Entity ids in different tenants/subjects cannot overwrite or suppress one another.
    /// </summary>
    public static string MakeId(string entityId)
        => MakeId(entityId, null);

    /// <summary>
    /// Creates the durable queue identity for one Entity in the supplied captured Koan context.
    /// </summary>
    public static string MakeId(
        string entityId,
        IReadOnlyDictionary<string, string>? capturedContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        var entityType = typeof(TEntity).Name;
        if (capturedContext is null || capturedContext.Count == 0)
            return $"embedjob:{entityType}:{entityId}";

        var fingerprint = KoanContextFingerprint.Compute(
            capturedContext,
            typeof(TEntity).FullName ?? entityType,
            entityId);
        return $"koan-context-embedjob:v1:{fingerprint}";
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
