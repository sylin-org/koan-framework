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

// Note: AiHealthCheckConfig and AiCircuitBreakerConfig are defined in AiGroupDefinition.cs
