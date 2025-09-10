namespace Sora.Core.Observability.Health;

/// <summary>
/// Event args for when a health probe is scheduled for a specific component
/// </summary>
public record ProbeScheduledEventArgs
{
    public string Component { get; init; } = "";
    public string Reason { get; init; } = "";
    public DateTimeOffset ScheduledAt { get; init; }
}

/// <summary>
/// Event args for when a health probe broadcast is sent to all components
/// </summary>
public record ProbeBroadcastEventArgs
{
    public int ComponentCount { get; init; }
    public DateTimeOffset BroadcastAt { get; init; }
}