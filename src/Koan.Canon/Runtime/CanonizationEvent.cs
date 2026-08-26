using Koan.Canon;

namespace Koan.Canon;

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
    public string Message { get; init; } = "";

    /// <summary>
    /// Who stopped the record, when this event parks it — the verb stamps
    /// <see cref="HoldReason.Refused"/>; engine-detected stalls default to
    /// <see cref="HoldReason.Stalled"/>.
    /// </summary>
    public HoldReason? Reason { get; init; }

    /// <summary>
    /// Optional structured detail.
    /// </summary>
    public string? Detail { get; init; }
}
