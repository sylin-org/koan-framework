namespace Sora.Messaging;

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