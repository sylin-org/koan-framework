namespace Koan.Core.Orchestration;

/// <summary>
/// Resolves connection strings based on orchestration mode and adapter hints.
/// Provides intelligent connection string selection for containerizable external resources.
/// </summary>
public interface IOrchestrationAwareConnectionResolver
{
    /// <summary>
    /// Resolves connection string for a service based on orchestration mode and adapter hints.
    /// </summary>
    /// <param name="serviceName">The logical service name (e.g., "redis", "postgres")</param>
    /// <param name="hints">Adapter-specific connection hints for different orchestration modes</param>
    /// <returns>Appropriate connection string for the current orchestration mode</returns>
    string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints);
}

/// <summary>
/// Connection hints for different orchestration modes.
/// Allows adapters to specify preferred hostnames and ports for each mode.
/// </summary>
public record OrchestrationConnectionHints
{
    /// <summary>
    /// Connection string for self-orchestration mode (app on host, dependencies in containers)
    /// Default: localhost:{port}
    /// </summary>
    public string? SelfOrchestrated { get; init; }

    /// <summary>
    /// Connection string for Docker Compose mode (app and dependencies in containers)
    /// Default: {serviceName}:{port}
    /// </summary>
    public string? DockerCompose { get; init; }

    /// <summary>
    /// Connection string for Kubernetes mode (app pod with service-based dependencies)
    /// Default: {serviceName}.default.svc.cluster.local:{port}
    /// </summary>
    public string? Kubernetes { get; init; }

    /// <summary>
    /// Connection string for Aspire AppHost mode (Aspire-managed endpoints)
    /// Default: null (Aspire will provide via service discovery)
    /// </summary>
    public string? AspireManaged { get; init; }

    /// <summary>
    /// Connection string for standalone mode (external dependencies)
    /// Default: null (must be explicitly configured)
    /// </summary>
    public string? External { get; init; }

    /// <summary>
    /// Default port for the service (used in fallback scenarios)
    /// </summary>
    public int DefaultPort { get; init; }

    /// <summary>
    /// Service name used for container/service discovery (defaults to serviceName parameter)
    /// </summary>
    public string? ServiceName { get; init; }
}