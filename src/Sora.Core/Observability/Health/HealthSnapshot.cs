namespace Sora.Core.Observability.Health;

public sealed record HealthSnapshot(
    HealthStatus Overall,
    IReadOnlyList<HealthSample> Components,
    DateTimeOffset ComputedAtUtc
);
