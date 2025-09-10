namespace Sora.Scheduling;

/// <summary>
/// Event args for when a scheduled task executes successfully
/// </summary>
public record TaskExecutedEventArgs
{
    public string TaskId { get; init; } = "";
    public DateTimeOffset ExecutedAt { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Event args for when a scheduled task fails
/// </summary>
public record TaskFailedEventArgs
{
    public string TaskId { get; init; } = "";
    public string Error { get; init; } = "";
    public DateTimeOffset FailedAt { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>
/// Event args for when a scheduled task times out
/// </summary>
public record TaskTimeoutEventArgs
{
    public string TaskId { get; init; } = "";
    public DateTimeOffset TimeoutAt { get; init; }
    public TimeSpan TimeoutDuration { get; init; }
}