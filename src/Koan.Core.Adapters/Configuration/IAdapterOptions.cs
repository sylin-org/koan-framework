namespace Koan.Core.Adapters.Configuration;

/// <summary>
/// Common interface for adapter configuration options.
/// Defines standard settings shared across all data adapters.
/// </summary>
public interface IAdapterOptions
{
    /// <summary>
    /// Adapter readiness configuration for initialization and health monitoring
    /// </summary>
    IAdapterReadinessConfiguration Readiness { get; }

    /// <summary>
    /// Default page size for query operations when not explicitly specified
    /// </summary>
    int DefaultPageSize { get; set; }

    /// <summary>
    /// Maximum allowed page size to prevent resource exhaustion
    /// </summary>
    int MaxPageSize { get; set; }
}