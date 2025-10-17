using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;

namespace Koan.AI.Connector.Ollama.Options;

/// <summary>
/// Ollama options that implement IAdapterOptions for autonomous discovery integration.
/// This wraps the array-based OllamaServiceOptions structure for consistency with other providers.
/// </summary>
public sealed class OllamaOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    /// <summary>
    /// Base URL for auto-discovered Ollama instances (for backward compatibility)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Default model to use for discovered instances
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Whether to enable automatic model downloading if required models are missing
    /// </summary>
    public bool AutoDownloadModels { get; set; } = true;

    /// <summary>
    /// Timeout for model download operations (in minutes)
    /// </summary>
    public int ModelDownloadTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Timeout for AI inference requests (in seconds). Default is 180 seconds (3 minutes) to accommodate vision models.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Whether auto-discovery is enabled
    /// </summary>
    public bool AutoDiscoveryEnabled { get; set; } = true;

    /// <summary>
    /// Weight for auto-discovered instances
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// Labels for auto-discovered instances
    /// </summary>
    public Dictionary<string, string>? Labels { get; set; }

    // IAdapterOptions implementation - AI processing batch properties
    public int DefaultPageSize { get; set; } = 10;
    public int MaxPageSize { get; set; } = 100;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
