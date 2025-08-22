namespace Sora.Core;

public sealed record HealthSnapshot(
    HealthStatus Overall,
    IReadOnlyList<HealthSample> Components,
    DateTimeOffset AsOfUtc
);