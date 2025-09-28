namespace Koan.Core.Orchestration.Abstractions;

/// <summary>
/// Pure coordination layer - delegates to registered adapters without provider knowledge.
/// </summary>
public interface IServiceDiscoveryCoordinator
{
    /// <summary>Delegate discovery to registered adapter for service name</summary>
    Task<AdapterDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        DiscoveryContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get all registered adapters (for diagnostics)</summary>
    IServiceDiscoveryAdapter[] GetRegisteredAdapters();
}