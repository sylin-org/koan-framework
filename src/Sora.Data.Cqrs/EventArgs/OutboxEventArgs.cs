namespace Sora.Data.Cqrs;

/// <summary>
/// Event args for when outbox entries are successfully processed
/// </summary>
public record OutboxProcessedEventArgs
{
    public int ProcessedCount { get; init; }
    public int FailedCount { get; init; }
    public int BatchSize { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
}

/// <summary>
/// Event args for when outbox processing fails
/// </summary>
public record OutboxFailedEventArgs
{
    public string EntryId { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string Operation { get; init; } = "";
    public string Error { get; init; } = "";
    public DateTimeOffset FailedAt { get; init; }
    public Exception? Exception { get; init; }
}