namespace Sora.Orchestration.Models;

public sealed record HealthSpec(
    string? HttpEndpoint,
    TimeSpan? Interval,
    TimeSpan? Timeout,
    int? Retries
);