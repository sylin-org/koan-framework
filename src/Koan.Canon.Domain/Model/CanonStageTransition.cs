namespace Koan.Canon.Domain.Model;

/// <summary>
/// Represents a state transition for a staged payload.
/// </summary>
public sealed class CanonStageTransition
{
    /// <summary>
    /// Initializes a new transition instance.
    /// </summary>
    public CanonStageTransition()
    {
    }

    /// <summary>
    /// Initializes a transition with the provided parameters.
    /// </summary>
    public CanonStageTransition(CanonStageStatus status, DateTimeOffset occurredAt, string? actor, string? notes)
    {
        Status = status;
        OccurredAt = occurredAt;
        Actor = actor;
        Notes = notes;
    }

    /// <summary>
    /// Status after the transition.
    /// </summary>
    public CanonStageStatus Status { get; set; }

    /// <summary>
    /// Time when the transition took place.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Optional actor responsible for the transition.
    /// </summary>
    public string? Actor { get; set; }

    /// <summary>
    /// Optional human-readable notes.
    /// </summary>
    public string? Notes { get; set; }
}
