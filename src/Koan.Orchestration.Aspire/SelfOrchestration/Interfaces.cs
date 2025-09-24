using Koan.Core.Orchestration;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Manages Docker containers for dependency orchestration
/// </summary>
public interface IKoanContainerManager
{
    /// <summary>
    /// Starts a Docker container for the specified dependency
    /// </summary>
    Task<string> StartContainerAsync(DependencyDescriptor dependency, string appInstance, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running Docker container
    /// </summary>
    Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a container to become healthy
    /// </summary>
    Task<bool> WaitForContainerHealthyAsync(string containerId, DependencyDescriptor dependency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up all containers for the given session
    /// </summary>
    Task CleanupSessionContainersAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up orphaned Koan containers from previous sessions
    /// </summary>
    Task CleanupOrphanedKoanContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up containers from crashed app instances
    /// </summary>
    Task CleanupAppInstanceContainersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates dependencies for self-managed Koan applications
/// </summary>
public interface IKoanDependencyOrchestrator
{
    /// <summary>
    /// Discovers and starts all required dependencies
    /// </summary>
    Task<List<string>> StartDependenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops all managed dependencies
    /// </summary>
    Task StopDependenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of currently managed dependencies
    /// </summary>
    Task<List<DependencyDescriptor>> GetManagedDependenciesAsync(CancellationToken cancellationToken = default);
}