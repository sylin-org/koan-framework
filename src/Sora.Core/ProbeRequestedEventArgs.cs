namespace Sora.Core;

public sealed class ProbeRequestedEventArgs : EventArgs
{
    public string? Component { get; init; }
    public ProbeReason Reason { get; init; }
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTimeOffset? NotAfterUtc { get; init; }
}