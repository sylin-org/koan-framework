using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;

namespace Koan.AI.Connector.LMStudio.Options;

/// <summary>
/// LM Studio options that implement IAdapterOptions for autonomous discovery integration.
/// </summary>
public sealed class LMStudioOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // discover local instance when possible

    /// <summary>
    /// Base URL fallback when discovery cannot locate a live LM Studio instance.
    /// </summary>
    public string BaseUrl { get; set; } = $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";

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

    /// <summary>
    /// Enables autonomous discovery (host-first, container-second) resolution when ConnectionString is "auto".
    /// </summary>
    public bool AutoDiscoveryEnabled { get; set; } = true;

    /// <summary>
    /// Weight assigned to auto-discovered members when registered with the AI router.
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// Labels propagated to the AI router for capability matching.
    /// </summary>
    public Dictionary<string, string>? Labels { get; set; }

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();

    // Adapter paging defaults (required by interface)
    public int DefaultPageSize { get; set; } = 10;
    public int MaxPageSize { get; set; } = 100;
}

