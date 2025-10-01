namespace Koan.AI.Contracts.Options;

/// <summary>
/// Base options for all AI capability requests.
/// Provides source/provider/model override mechanisms.
/// </summary>
public abstract record AiOptionsBase
{
    /// <summary>
    /// Override source or group name for this request.
    /// Examples: "ollama-primary", "production-ollama", "cloud-services"
    /// Takes precedence over context and routing configuration.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Fallback: specify provider type directly (when source not specified).
    /// Examples: "ollama", "openai", "anthropic"
    /// Less preferred than Source - use Source for production scenarios.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Escape hatch: override model directly (bypasses source capability mapping).
    /// Examples: "llama3.2:70b", "gpt-4o", "claude-3-5-sonnet-20250219"
    /// Use sparingly - prefer source-based configuration for maintainability.
    /// </summary>
    public string? Model { get; init; }
}
