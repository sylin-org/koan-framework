namespace Sora.Messaging.Infrastructure;

// Centralized message header keys used across transports. Avoid magic strings in adapters.
public static class HeaderNames
{
    public const string IdempotencyKey = "x-idempotency-key";
    public const string CorrelationId = "x-correlation-id";
    public const string CausationId = "x-causation-id";
    public const string Attempt = "x-attempt";
    public const string RetryBucket = "x-retry-bucket";
}
