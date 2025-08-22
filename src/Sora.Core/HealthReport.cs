namespace Sora.Core;

// Health primitives (legacy contributor contract kept for bridge only)

public sealed record HealthReport(string Name, HealthState State, string? Description = null, Exception? Exception = null, IReadOnlyDictionary<string, object?>? Data = null);

// Note: Legacy IHealthService removed. Health is computed via IHealthAggregator exclusively.
