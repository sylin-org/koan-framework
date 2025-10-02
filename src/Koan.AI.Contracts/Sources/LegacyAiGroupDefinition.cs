using System;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// DEPRECATED: Old AiGroupDefinition (inverted terminology - this was actually a source collection).
/// Kept temporarily for migration. Will be removed.
/// Use AiSourceDefinition instead (which now includes Policy and Members).
/// </summary>
[Obsolete("Use AiSourceDefinition - groups are now sources with member collections")]
public sealed record LegacyAiGroupDefinition
{
    public required string Name { get; init; }
    public string Policy { get; init; } = "Fallback";
    public AiHealthCheckConfig HealthCheck { get; init; } = new();
    public AiCircuitBreakerConfig CircuitBreaker { get; init; } = new();
    public bool StickySession { get; init; }
}

public sealed record AiHealthCheckConfig
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 30;
    public int TimeoutSeconds { get; init; } = 5;
}

public sealed record AiCircuitBreakerConfig
{
    public int FailureThreshold { get; init; } = 3;
    public int BreakDurationSeconds { get; init; } = 30;
    public int RecoveryThreshold { get; init; } = 2;
}
