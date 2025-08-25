namespace Sora.Messaging;

public sealed class EffectiveMessagingPlan
{
    public required string BusCode { get; init; }
    public required string Provider { get; init; }
    public required IMessagingCapabilities Capabilities { get; init; }
    public string DelayMode { get; init; } = "native|ttl|inline|none";
    public bool DlqEnabled { get; init; }
    public RetryOptions Retry { get; init; } = new();
}