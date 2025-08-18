namespace Sora.Messaging;

public sealed class RetryOptions
{
    public int MaxAttempts { get; set; } = 5;
    public string Backoff { get; set; } = "exponential"; // or fixed
    public int FirstDelaySeconds { get; set; } = 3;
    public int MaxDelaySeconds { get; set; } = 60;
}

public sealed class DlqOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class EffectiveMessagingPlan
{
    public required string BusCode { get; init; }
    public required string Provider { get; init; }
    public required IMessagingCapabilities Capabilities { get; init; }
    public string DelayMode { get; init; } = "native|ttl|inline|none";
    public bool DlqEnabled { get; init; }
    public RetryOptions Retry { get; init; } = new();
}

public static class Negotiation
{
    public static EffectiveMessagingPlan BuildPlan(string busCode, string provider, IMessagingCapabilities caps, RetryOptions retry, DlqOptions dlq, bool requestDelay)
    {
        var delayMode = "none";
        if (requestDelay && caps.DelayedDelivery) delayMode = "native";
        else if (requestDelay && caps.DeadLettering) delayMode = "ttl"; // via TTL+DLX
        else if (requestDelay && !caps.DelayedDelivery) delayMode = "inline"; // last resort

        var dlqEnabled = dlq.Enabled && caps.DeadLettering;
        if (dlq.Enabled && !caps.DeadLettering)
        {
            // reduce attempts if DLQ missing
            retry = new RetryOptions { MaxAttempts = Math.Min(1, retry.MaxAttempts), Backoff = retry.Backoff, FirstDelaySeconds = retry.FirstDelaySeconds, MaxDelaySeconds = retry.MaxDelaySeconds };
        }

        return new EffectiveMessagingPlan
        {
            BusCode = busCode,
            Provider = provider,
            Capabilities = caps,
            DelayMode = delayMode,
            DlqEnabled = dlqEnabled,
            Retry = retry
        };
    }
}

public interface IMessagingDiagnostics
{
    void SetEffectivePlan(string busCode, EffectiveMessagingPlan plan);
    EffectiveMessagingPlan? GetEffectivePlan(string busCode);
}

internal sealed class MessagingDiagnostics : IMessagingDiagnostics
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EffectiveMessagingPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    public void SetEffectivePlan(string busCode, EffectiveMessagingPlan plan) => _plans[busCode] = plan;
    public EffectiveMessagingPlan? GetEffectivePlan(string busCode) => _plans.TryGetValue(busCode, out var p) ? p : null;
}
