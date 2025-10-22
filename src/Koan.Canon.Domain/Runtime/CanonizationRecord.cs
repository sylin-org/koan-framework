using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Represents a persisted canonization event for replay or analytics.
/// </summary>
public sealed class CanonizationRecord
{
    /// <summary>
    /// Canonical identifier associated with the record.
    /// </summary>
    public string CanonicalId { get; init; } = string.Empty;

    /// <summary>
    /// CLR type name of the canonical entity.
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>
    /// Pipeline phase that produced the record.
    /// </summary>
    public CanonPipelinePhase Phase { get; init; }

    /// <summary>
    /// Stage status captured with the record.
    /// </summary>
    public CanonStageStatus StageStatus { get; init; }

    /// <summary>
    /// Overall outcome at the time of the record.
    /// </summary>
    public CanonizationOutcome Outcome { get; init; }

    /// <summary>
    /// Timestamp for when the record was created.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Metadata snapshot at the time of the record.
    /// </summary>
    public CanonMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Detailed event payload.
    /// </summary>
    public CanonizationEvent? Event { get; init; }
}
