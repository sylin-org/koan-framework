using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Definition of an AI source - a named configuration combining provider, endpoint, and capability-to-model mappings.
/// Sources can be grouped for fallback/load-balancing policies.
///
/// Examples:
/// - "ollama-auto-host": Auto-discovered Ollama on host.docker.internal
/// - "ollama-primary": Explicitly configured production Ollama instance
/// - "openai-premium": OpenAI with GPT-4o for vision tasks
/// </summary>
public sealed record AiSourceDefinition
{
    /// <summary>
    /// Source name (unique identifier). Examples: "ollama-primary", "openai-premium", "ollama-auto-host"
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Provider type. Examples: "ollama", "openai", "anthropic"
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Connection string or base URL. Examples: "http://localhost:11434", "https://api.openai.com/v1"
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Group membership for fallback/load-balancing policies.
    /// Sources in same group form fallback chains or round-robin pools.
    /// Examples: "production-ollama", "ollama-auto"
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Priority within group (higher = preferred). Default: 50.
    /// Examples: host=100, linked=75, container=50
    /// </summary>
    public int Priority { get; init; } = 50;

    /// <summary>
    /// Capability-to-model mappings. Keys: "Chat", "Embedding", "Vision"
    /// </summary>
    public IReadOnlyDictionary<string, AiCapabilityConfig> Capabilities { get; init; }
        = new Dictionary<string, AiCapabilityConfig>();

    /// <summary>
    /// Additional provider-specific settings (e.g., API keys, timeouts, batch sizes)
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Source origin for diagnostics. Examples: "auto-discovery", "explicit-config", "environment-variable"
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Whether this source was auto-discovered (vs explicitly configured)
    /// </summary>
    public bool IsAutoDiscovered { get; init; }
}
