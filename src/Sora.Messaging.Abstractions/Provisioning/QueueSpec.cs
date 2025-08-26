namespace Sora.Messaging.Provisioning;

public sealed record QueueSpec(string Name, bool Durable = true, bool Exclusive = false, bool AutoDelete = false,
    IReadOnlyDictionary<string, object?>? Arguments = null,
    DlqSpec? Dlq = null,
    RetryBucketsSpec? Retry = null,
    int? MessageTtlMs = null);