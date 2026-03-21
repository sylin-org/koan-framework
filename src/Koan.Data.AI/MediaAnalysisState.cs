using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace Koan.Data.AI;

/// <summary>
/// Tracks the processing state of media analysis for an entity.
/// Stored alongside the entity, queryable for monitoring and retry.
/// Follows the same pattern as <see cref="EmbeddingState{TEntity}"/>.
/// </summary>
internal sealed class MediaAnalysisState<TEntity> : Entity<MediaAnalysisState<TEntity>>
    where TEntity : class
{
    /// <summary>
    /// ID of the entity this state tracks.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Overall processing status.
    /// </summary>
    public MediaAnalysisStatus Status { get; init; } = MediaAnalysisStatus.Pending;

    /// <summary>
    /// Attribute version when analysis was last completed.
    /// Used to detect version bumps that require re-analysis.
    /// </summary>
    public int AnalyzedVersion { get; init; }

    /// <summary>
    /// Number of processing attempts (for retry tracking).
    /// </summary>
    public int AttemptCount { get; init; }

    /// <summary>
    /// When the last processing attempt started.
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; init; }

    /// <summary>
    /// When analysis completed successfully.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Failure reason from the most recent attempt (null when succeeded).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Per-mode completion tracking. Allows partial completion
    /// (e.g., Describe succeeded but Ocr failed).
    /// </summary>
    public IReadOnlyDictionary<MediaAnalysis, ModeStatus> ModeStatuses { get; init; }
        = new Dictionary<MediaAnalysis, ModeStatus>();

    /// <summary>
    /// Creates ID for MediaAnalysisState: "media-analysis:{entityType}:{entityId}"
    /// </summary>
    public static string MakeId(string entityId)
    {
        var entityType = typeof(TEntity).Name;
        return $"media-analysis:{entityType}:{entityId}";
    }
}

/// <summary>
/// Overall analysis processing status.
/// </summary>
public enum MediaAnalysisStatus
{
    /// <summary>Entity queued but not yet picked up by worker.</summary>
    Pending,

    /// <summary>Explicitly queued for background processing.</summary>
    Queued,

    /// <summary>Worker is currently processing this entity.</summary>
    Processing,

    /// <summary>All requested analysis modes completed successfully.</summary>
    Completed,

    /// <summary>Some modes succeeded, others failed. Partial results available.</summary>
    PartiallyCompleted,

    /// <summary>All attempts exhausted or fatal error.</summary>
    Failed
}

/// <summary>
/// Completion status for an individual analysis mode.
/// </summary>
public sealed record ModeStatus(
    bool Completed,
    DateTimeOffset? CompletedAt,
    string? Error);
