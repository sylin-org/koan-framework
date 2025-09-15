namespace Koan.Core.Observability.Probes;

public sealed class ProbeRequestedEventArgs : EventArgs
{
    public string? Component { get; init; }
    public ProbeReason Reason { get; init; }
    public Guid CorrelationId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset? NotAfterUtc { get; init; }
}
