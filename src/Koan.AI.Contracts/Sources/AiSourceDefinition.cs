using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Definition of an AI source - a named collection of members with shared priority, policy, and routing rules.
/// Sources represent logical groupings of AI endpoints with automatic failover and load balancing.
///
/// Examples:
/// - "ollama": Auto-created by adapter with auto-discovered members (priority 50)
/// - "enterprise": Explicitly configured production endpoints (priority 100)
///
/// ADR-0015: Canonical Source-Member Architecture
/// </summary>
public sealed record AiSourceDefinition
{
    /// <summary>
    /// Source name (unique identifier).
    /// Examples: "ollama", "enterprise", "production"
    /// MUST NOT contain "::" separator (reserved for members).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Provider type for all members in this source.
    /// Examples: "ollama", "openai", "anthropic"
    /// Used to resolve adapter via IAiAdapterRegistry.Get(Provider)
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Priority for source election (higher = preferred). Default: 50
    /// Priority scale:
    /// - 100+: Explicit user configuration (highest)
    /// - 50: Adapter-provided auto-discovery (default)
    /// - 0-49: Degraded/fallback sources
    /// </summary>
    public int Priority { get; init; } = 50;

    /// <summary>
    /// Member selection policy for this source. Default: "Fallback"
    /// Options: "Fallback" (priority-based), "RoundRobin", "WeightedRoundRobin"
    ///
    /// Policy precedence (least → most specific):
    /// 1. Koan:Ai:Policy (global)
    /// 2. Koan:Ai:{adapter}:Policy (adapter-level)
    /// 3. Koan:Ai:Sources:{source}:Policy (source-specific)
    /// </summary>
    public string Policy { get; init; } = "Fallback";

    /// <summary>
    /// Members (endpoints) in this source.
    /// Must have at least one member.
    /// Members named with source::identifier pattern (e.g., "ollama::host").
    /// </summary>
    public required List<AiMemberDefinition> Members { get; init; }

    /// <summary>
    /// Shared capabilities for all members in this source.
    /// Keys: "Chat", "Embedding", "Vision"
    /// Individual members can override with member-specific capabilities.
    /// </summary>
    public IReadOnlyDictionary<string, AiCapabilityConfig> Capabilities { get; init; }
        = new Dictionary<string, AiCapabilityConfig>();

    /// <summary>
    /// Circuit breaker configuration for members in this source.
    /// If null, uses global circuit breaker settings.
    /// </summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }

    /// <summary>
    /// Source origin for diagnostics.
    /// Examples: "auto-discovery", "explicit-config", "legacy-config"
    /// </summary>
    public string Origin { get; init; } = "explicit-config";

    /// <summary>
    /// Whether this source was auto-created by an adapter (vs explicitly configured)
    /// </summary>
    public bool IsAutoDiscovered { get; init; }

    /// <summary>
    /// Get effective capabilities for a member (member-specific overrides source-level)
    /// </summary>
    public IReadOnlyDictionary<string, AiCapabilityConfig> GetEffectiveCapabilities(AiMemberDefinition member)
    {
        if (member.Capabilities != null && member.Capabilities.Count > 0)
            return member.Capabilities;

        return Capabilities;
    }

    /// <summary>
    /// Get current health state of this source (aggregated from members)
    /// </summary>
    public SourceHealthState GetHealthState()
    {
        if (Members.Count == 0)
            return SourceHealthState.Unhealthy;

        var healthyCount = Members.Count(m => m.HealthState == MemberHealthState.Healthy);
        var totalCount = Members.Count;

        if (healthyCount == totalCount)
            return SourceHealthState.Healthy;

        if (healthyCount > 0)
            return SourceHealthState.Degraded;

        return SourceHealthState.Unhealthy;
    }
}

/// <summary>
/// Health state of a source (aggregated from members)
/// </summary>
public enum SourceHealthState
{
    /// <summary>All members healthy</summary>
    Healthy,

    /// <summary>Some members healthy, some unhealthy</summary>
    Degraded,

    /// <summary>No members healthy</summary>
    Unhealthy
}

/// <summary>
/// Circuit breaker configuration
/// </summary>
public sealed record CircuitBreakerConfig
{
    /// <summary>Number of consecutive failures before opening circuit. Default: 3</summary>
    public int FailureThreshold { get; init; } = 3;

    /// <summary>Duration to keep circuit open before attempting recovery (seconds). Default: 30</summary>
    public int BreakDurationSeconds { get; init; } = 30;

    /// <summary>Number of successful probes required to close circuit. Default: 2</summary>
    public int SuccessThreshold { get; init; } = 2;
}
