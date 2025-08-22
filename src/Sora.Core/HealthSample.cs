namespace Sora.Core;

public sealed record HealthSample(
    string Component,
    HealthStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc,
    TimeSpan? Ttl,
    IReadOnlyDictionary<string, string>? Facts
);