using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Definition of an AI member - an individual service endpoint within a source.
/// Members are named with source::identifier pattern (e.g., "ollama::host", "enterprise::ollama-1").
///
/// ADR-0015: Canonical Source-Member Architecture
/// </summary>
public sealed record AiMemberDefinition
{
    /// <summary>
    /// Member name with source::identifier pattern.
    /// Examples: "ollama::host", "enterprise::ollama-1", "ollama::gpu"
    /// MUST contain "::" separator.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Connection string (URL) for this member.
    /// Examples: "http://host.docker.internal:11434", "http://ollama1.corp:11434"
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Order for fallback policy within source (0 = first tried).
    /// Used by Fallback policy to determine member sequence.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Weight for weighted round-robin policy. Default: 1
    /// Higher weights receive proportionally more requests.
    /// </summary>
    public int Weight { get; init; } = 1;

    /// <summary>
    /// Member-specific capabilities (overrides source-level capabilities).
    /// Keys: "Chat", "Embedding", "Vision"
    /// If null, inherits capabilities from source.
    /// </summary>
    public IReadOnlyDictionary<string, AiCapabilityConfig>? Capabilities { get; init; }

    /// <summary>
    /// Whether this member was auto-discovered (vs explicitly configured)
    /// </summary>
    public bool IsAutoDiscovered { get; init; }

    /// <summary>
    /// Member origin for diagnostics.
    /// Examples: "discovered", "config-urls", "config-additional-urls"
    /// </summary>
    public string Origin { get; init; } = "discovered";

    /// <summary>
    /// Current health state. Set by health monitoring system.
    /// </summary>
    public MemberHealthState HealthState { get; set; } = MemberHealthState.Unknown;
}

/// <summary>
/// Health state of a member endpoint
/// </summary>
public enum MemberHealthState
{
    /// <summary>Not yet probed</summary>
    Unknown,

    /// <summary>Circuit closed, passing health checks</summary>
    Healthy,

    /// <summary>Circuit open due to failures</summary>
    Unhealthy,

    /// <summary>Circuit half-open, testing recovery</summary>
    Recovering
}
