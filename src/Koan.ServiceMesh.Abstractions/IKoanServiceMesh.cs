namespace Koan.ServiceMesh.Abstractions;

/// <summary>
/// Service mesh interface for discovery and announcement.
/// Internal framework interface - not exposed to service consumers.
/// </summary>
public interface IKoanServiceMesh
{
    /// <summary>
    /// Announce this service to the orchestrator channel.
    /// Called on startup and periodically for heartbeats.
    /// </summary>
    Task AnnounceAsync(CancellationToken ct = default);

    /// <summary>
    /// Discover available services by broadcasting request to orchestrator channel.
    /// </summary>
    Task DiscoverAsync(CancellationToken ct = default);

    /// <summary>
    /// Maintain service mesh (listen for announcements, clean stale instances).
    /// Runs continuously in background.
    /// </summary>
    Task MaintainAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a service instance for the specified service ID.
    /// Uses load balancing policy to select instance if multiple available.
    /// </summary>
    /// <param name="serviceId">Service identifier (e.g., "translation").</param>
    /// <param name="policy">Load balancing policy.</param>
    /// <returns>Service instance or null if not found.</returns>
    ServiceInstance? GetInstance(string serviceId, LoadBalancingPolicy policy = LoadBalancingPolicy.RoundRobin);

    /// <summary>
    /// Get all instances of the specified service.
    /// </summary>
    /// <param name="serviceId">Service identifier (e.g., "translation").</param>
    /// <returns>List of all instances (may be empty).</returns>
    IReadOnlyList<ServiceInstance> GetAllInstances(string serviceId);

    /// <summary>
    /// Get all discovered services (service IDs).
    /// </summary>
    /// <returns>List of discovered service IDs.</returns>
    IReadOnlyList<string> GetDiscoveredServices();
}

/// <summary>
/// Load balancing policy for service instance selection.
/// </summary>
public enum LoadBalancingPolicy
{
    /// <summary>
    /// Distribute requests evenly across instances (default).
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Random instance selection.
    /// </summary>
    Random,

    /// <summary>
    /// Route to instance with fewest active connections.
    /// </summary>
    LeastConnections,

    /// <summary>
    /// Route to healthiest instance based on response time and status.
    /// </summary>
    HealthAware
}
