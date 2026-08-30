using Koan.Core.Adapters;

namespace Koan.AI.Connector.LlamaCpp.Options;

/// <summary>
/// llama.cpp (<c>llama-server</c>) connection, routing, and readiness options.
/// </summary>
public sealed class LlamaCppOptions
{
    /// <summary>
    /// Exact llama-server endpoints. When empty, Koan discovers one conventional local/container endpoint.
    /// </summary>
    public string[] Endpoints { get; set; } = [];

    /// <summary>
    /// Optional API key forwarded as Bearer token when llama-server runs with <c>--api-key</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default model used when callers omit an explicit model id. llama-server serves the model it
    /// was started with; a mismatching request is refused by the server rather than substituted.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Timeout for inference requests in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 120;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
