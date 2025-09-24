namespace Koan.Core;

/// <summary>
/// Defines the orchestration modes based on runtime context and environment.
/// Determines both dependency management strategy and networking approach.
/// </summary>
public enum OrchestrationMode
{
    /// <summary>
    /// No orchestration - using external dependencies (production mode)
    /// Network: External service endpoints (cloud services, managed databases)
    /// Context: Production environment with pre-existing infrastructure
    /// </summary>
    Standalone,

    /// <summary>
    /// Self-orchestration - host application spawns dependency containers
    /// Network: localhost endpoints for spawned containers (host networking)
    /// Context: Development host machine with Docker available
    /// Detection: !InContainer and IsDevelopment and DockerAvailable
    /// </summary>
    SelfOrchestrating,

    /// <summary>
    /// Docker Compose orchestration - app and dependencies in container network
    /// Network: service name endpoints (postgres:5432, redis:6379)
    /// Context: App running in container alongside dependency containers
    /// Detection: InContainer and DockerComposeContext
    /// </summary>
    DockerCompose,

    /// <summary>
    /// Kubernetes orchestration - app pod with service-based dependencies
    /// Network: Kubernetes service DNS (postgres.default.svc.cluster.local:5432)
    /// Context: App running in Kubernetes pod
    /// Detection: InContainer and KubernetesContext
    /// </summary>
    Kubernetes,

    /// <summary>
    /// Aspire AppHost orchestration - external AppHost manages dependencies
    /// Network: Aspire-managed endpoints with service discovery
    /// Context: App launched by Aspire AppHost with resource management
    /// Detection: AspireEnvironmentVariables detected (highest priority)
    /// </summary>
    AspireAppHost
}