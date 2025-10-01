using System;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Definition of an AI source group with policy and health monitoring configuration.
/// Groups enable fallback chains, round-robin load balancing, and automatic recovery.
/// </summary>
public sealed record AiGroupDefinition
{
    /// <summary>
    /// Group name. Examples: "production-ollama", "ollama-auto", "cloud-services"
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Routing policy for this group. Default: "Fallback"
    /// Options: "Fallback" (priority-based), "RoundRobin", "WeightedRoundRobin"
    /// </summary>
    public string Policy { get; init; } = "Fallback";

    /// <summary>
    /// Health monitoring configuration
    /// </summary>
    public AiHealthCheckConfig HealthCheck { get; init; } = new();

    /// <summary>
    /// Circuit breaker configuration
    /// </summary>
    public AiCircuitBreakerConfig CircuitBreaker { get; init; } = new();

    /// <summary>
    /// Whether to use sticky sessions (for round-robin). Default: false
    /// </summary>
    public bool StickySession { get; init; }
}

/// <summary>
/// Health monitoring configuration for a group
/// </summary>
public sealed record AiHealthCheckConfig
{
    /// <summary>
    /// Whether health monitoring is enabled. Default: true
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Health probe interval in seconds. Default: 30
    /// </summary>
    public int IntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Health check timeout in seconds. Default: 5
    /// </summary>
    public int TimeoutSeconds { get; init; } = 5;
}

/// <summary>
/// Circuit breaker configuration for a group
/// </summary>
public sealed record AiCircuitBreakerConfig
{
    /// <summary>
    /// Number of consecutive failures before opening circuit. Default: 3
    /// </summary>
    public int FailureThreshold { get; init; } = 3;

    /// <summary>
    /// Duration to keep circuit open before attempting recovery (seconds). Default: 30
    /// </summary>
    public int BreakDurationSeconds { get; init; } = 30;

    /// <summary>
    /// Number of successful probes required to close circuit. Default: 2
    /// </summary>
    public int RecoveryThreshold { get; init; } = 2;
}
