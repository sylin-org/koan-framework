using Koan.Core.Observability.Health;
using Koan.Orchestration.Models;

namespace Koan.Core.Adapters;

/// <summary>
/// Core interface for all Koan adapters providing unified capability declaration,
/// health reporting, and bootstrap data integration.
/// </summary>
public interface IKoanAdapter : IHealthContributor
{
    /// <summary>
    /// Service type category for orchestration-aware tooling
    /// </summary>
    ServiceType ServiceType { get; }

    /// <summary>
    /// Unique adapter identifier for registration and discovery
    /// </summary>
    string AdapterId { get; }

    /// <summary>
    /// Human-readable display name for logging and diagnostics
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Comprehensive capability declaration for runtime querying
    /// </summary>
    AdapterCapabilities Capabilities { get; }

    /// <summary>
    /// Bootstrap data for initial discovery and logging integration
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> GetBootstrapMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialize the adapter with configuration and dependencies
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the adapter supports a specific capability
    /// </summary>
    bool SupportsCapability<T>(T capability) where T : Enum;
}