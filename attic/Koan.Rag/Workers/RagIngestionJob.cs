using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Rag.Abstractions;

namespace Koan.Rag.Workers;

/// <summary>
/// Tracks per-entity ingestion state for idempotency, retry, and observability.
/// Stored in <c>.Koan/</c> persistent store (not the user's primary data store).
/// Follows the <c>EmbedJob&lt;T&gt;</c> pattern.
/// </summary>
internal sealed class RagIngestionJob : Entity<RagIngestionJob>
{
    /// <summary>The entity ID being ingested.</summary>
    public required string EntityId { get; set; }

    /// <summary>The entity type name (e.g., "Policy").</summary>
    public required string EntityType { get; set; }

    /// <summary>Corpus name (null for default).</summary>
    public string? CorpusName { get; set; }

    /// <summary>SHA-256 hash of the entity's embeddable content.</summary>
    public required string ContentSignature { get; set; }

    /// <summary>Processing status.</summary>
    public required RagIngestionStatus Status { get; set; }

    /// <summary>Number of retry attempts.</summary>
    public int RetryCount { get; set; }

    /// <summary>Maximum retries before permanent failure.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Last error message (null on success).</summary>
    public string? Error { get; set; }

    /// <summary>When the job was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When processing started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When processing completed (success or permanent failure).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Chunks created during this ingestion.</summary>
    public int ChunksCreated { get; set; }

    /// <summary>Entities extracted during this ingestion.</summary>
    public int EntitiesExtracted { get; set; }

    /// <summary>Processing priority (higher = processed first).</summary>
    public int Priority { get; set; }

    /// <summary>
    /// Deterministic job ID from entity type, corpus name, and entity ID.
    /// </summary>
    public static string MakeId(string entityType, string? corpusName, string entityId)
        => $"ragjob:{entityType}:{corpusName ?? "default"}:{entityId}";
}
