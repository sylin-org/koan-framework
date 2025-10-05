using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Captures telemetry about a pipeline phase during canonization.
/// </summary>
public sealed class CanonizationEvent
{
    /// <summary>
    /// Canonization phase the event relates to.
    /// </summary>
    public CanonPipelinePhase Phase { get; init; }

    /// <summary>
    /// Stage status after the phase.
    /// </summary>
    public CanonStageStatus StageStatus { get; init; }

    /// <summary>
    /// Canonical entity state after the phase.
    /// </summary>
    public CanonState? CanonState { get; init; }

    /// <summary>
    /// Timestamp for the event.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Human readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional structured detail.
    /// </summary>
    public string? Detail { get; init; }
}
