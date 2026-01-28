using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Client for Zen Garden service discovery.
/// Handles UDP broadcast, Stone HTTP API, and caching.
/// </summary>
public interface IZenGardenClient : IDisposable
{
    /// <summary>
    /// Discover all Stones on the network via UDP multicast.
    /// </summary>
    Task<IReadOnlyList<Stone>> DiscoverStonesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find a service by offering name.
    /// Uses two-level caching: bound Stone + offering cache.
    /// </summary>
    Task<ResolvedService?> FindServiceAsync(
        string offering,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a specific running service from a Stone.
    /// Uses GET /api/v1/services/{name} endpoint.
    /// </summary>
    Task<ServiceInfo?> GetServiceAsync(
        Stone stone,
        string serviceName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// List all running services on a Stone.
    /// Uses GET /api/v1/services endpoint.
    /// </summary>
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(
        Stone stone,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a Stone is reachable.
    /// Uses GET /health endpoint.
    /// </summary>
    Task<bool> IsStoneHealthyAsync(
        Stone stone,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invalidate a cached offering - will re-query on next request.
    /// Call this when a service connection fails.
    /// </summary>
    void InvalidateOffering(string offering);
    
    /// <summary>
    /// Invalidate the bound Stone - will re-discover on next request.
    /// Call this when Moss connection fails.
    /// </summary>
    void InvalidateStone();
    
    /// <summary>
    /// Get the currently bound Stone (if any).
    /// </summary>
    Stone? BoundStone { get; }
}
