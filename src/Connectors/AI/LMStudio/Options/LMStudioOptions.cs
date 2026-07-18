using Koan.Core.Adapters;

namespace Koan.AI.Connector.LMStudio.Options;

/// <summary>
/// LM Studio connection, routing, and readiness options.
/// </summary>
public sealed class LMStudioOptions
{
    /// <summary>
    /// Exact LM Studio endpoints. When empty, Koan discovers one conventional local/container endpoint.
    /// </summary>
    public string[] Endpoints { get; set; } = [];

    /// <summary>
    /// Optional API key forwarded as Bearer token when LM Studio enforces authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default model used when callers omit an explicit model id.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Timeout for inference requests in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 120;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
