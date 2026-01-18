namespace Koan.AI.Connector.Ollama.Options;

/// <summary>
/// Configuration options for Ollama adapter.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>
    /// Default model to use for chat and embeddings.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Timeout for AI inference requests (in seconds). Default is 180 seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Maximum number of concurrent requests allowed against a single Ollama endpoint.
    /// Set to 0 to disable throttling. Default is 3.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;
}
