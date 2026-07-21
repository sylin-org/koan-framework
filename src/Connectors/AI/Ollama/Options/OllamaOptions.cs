namespace Koan.AI.Connector.Ollama.Options;

/// <summary>
/// Configuration options for Ollama adapter.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>
    /// Exact Ollama endpoints. When empty, Koan discovers one conventional local/container endpoint.
    /// </summary>
    public string[] Endpoints { get; set; } = [];

    /// <summary>
    /// Default model to use for chat and embeddings.
    /// </summary>
    public string DefaultModel { get; set; } = "llama3.2";

    /// <summary>Optional capabilities passed to layered discovery (for example model names).</summary>
    public string[] RequiredCapabilities { get; set; } = [];

    /// <summary>
    /// Maximum number of concurrent requests allowed against a single Ollama endpoint.
    /// Set to 0 to disable throttling. Default is 3.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;
}
