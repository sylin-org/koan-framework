namespace Sora.Core.Observability.Health;

public sealed record HealthReport(
    string Name,
    HealthState State,
    string? Description,
    TimeSpan? Ttl,
    IReadOnlyDictionary<string, object?>? Data
);