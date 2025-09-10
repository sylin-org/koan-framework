namespace Sora.Messaging;

/// <summary>
/// Event args for when messaging system is ready and operational
/// </summary>
public record MessagingReadyEventArgs
{
    public int HandlerCount { get; init; }
    public string ProviderName { get; init; } = "";
    public DateTimeOffset ReadyAt { get; init; }
}

/// <summary>
/// Event args for when messaging system fails to start or operate
/// </summary>
public record MessagingFailedEventArgs
{
    public string Reason { get; init; } = "";
    public DateTimeOffset FailedAt { get; init; }
    public Exception? Exception { get; init; }
}