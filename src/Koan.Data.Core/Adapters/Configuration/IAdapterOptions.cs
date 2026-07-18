using Koan.Core.Adapters;

namespace Koan.Data.Adapters.Configuration;

/// <summary>
/// Common interface for adapter configuration options.
/// Defines readiness settings shared across all data adapters.
/// </summary>
public interface IAdapterOptions
{
    /// <summary>
    /// Adapter readiness configuration for initialization and health monitoring
    /// </summary>
    IAdapterReadinessConfiguration Readiness { get; }
}
