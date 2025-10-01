using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Configuration for a specific AI capability (Chat, Embedding, Vision, etc.)
/// Maps capability to model name and optional parameters.
/// </summary>
public sealed record AiCapabilityConfig
{
    /// <summary>
    /// Model name to use for this capability. Examples: "llama3.2", "gpt-4o", "nomic-embed-text"
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Optional capability-specific options (temperature, max_tokens, etc.)
    /// </summary>
    public IReadOnlyDictionary<string, object>? Options { get; init; }

    /// <summary>
    /// Whether to automatically download this model if missing (Ollama only). Default: true
    /// </summary>
    public bool AutoDownload { get; init; } = true;
}
