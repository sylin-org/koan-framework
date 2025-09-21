namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Manages Docker containers for dependency orchestration
/// </summary>
public interface IKoanContainerManager
{
    /// <summary>
    /// Starts a Docker container for the specified dependency
    /// </summary>
    Task<string> StartContainerAsync(DependencyDescriptor dependency, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running Docker container
    /// </summary>
    Task StopContainerAsync(string containerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Docker is available on the system
    /// </summary>
    Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a container is currently running
    /// </summary>
    Task<bool> IsContainerRunningAsync(string containerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a container to become healthy
    /// </summary>
    Task<bool> WaitForContainerHealthyAsync(string containerName, DependencyDescriptor dependency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up containers for a specific session
    /// </summary>
    Task CleanupSessionContainersAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up containers from crashed app instances
    /// </summary>
    Task CleanupAppInstanceContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up orphaned Koan containers
    /// </summary>
    Task CleanupOrphanedKoanContainersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates dependencies for self-hosted Koan applications
/// </summary>
public interface IKoanDependencyOrchestrator
{
    /// <summary>
    /// Starts all required dependencies
    /// </summary>
    Task StartDependenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops all managed dependencies
    /// </summary>
    Task StopDependenciesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a dependency that can be self-orchestrated
/// </summary>
public class DependencyDescriptor
{
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public Dictionary<string, string> Environment { get; set; } = new();
    public Dictionary<int, int> Ports { get; set; } = new();
    public List<string> Volumes { get; set; } = new();
    public string NetworkMode { get; set; } = "";
    public int HealthCheckRetries { get; set; } = 5;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // Additional properties used by the implementation
    public int Port { get; set; }
    public int? TargetPort { get; set; }
    public string HealthCheckCommand { get; set; } = "";
    public int StartupPriority { get; set; } = 0;
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // Static dependency descriptors for common services
    public static readonly DependencyDescriptor PostgresDb = new()
    {
        Name = "postgres",
        Image = "postgres:15-alpine",
        Port = 5432,
        HealthCheckCommand = "pg_isready -U koan",
        StartupPriority = 1,
        HealthTimeout = TimeSpan.FromMinutes(2),
        Environment = new Dictionary<string, string>
        {
            ["POSTGRES_DB"] = "koan",
            ["POSTGRES_USER"] = "koan",
            ["POSTGRES_PASSWORD"] = "dev123"
        }
    };

    public static readonly DependencyDescriptor Redis = new()
    {
        Name = "redis",
        Image = "redis:7-alpine",
        Port = 6379,
        HealthCheckCommand = "redis-cli ping",
        StartupPriority = 1,
        HealthTimeout = TimeSpan.FromMinutes(1)
    };

    public static readonly DependencyDescriptor Ollama = new()
    {
        Name = "ollama",
        Image = "ollama/ollama:latest",
        Port = 11434,
        StartupPriority = 2,
        HealthTimeout = TimeSpan.FromMinutes(3)
    };

    public static readonly DependencyDescriptor Weaviate = new()
    {
        Name = "weaviate",
        Image = "cr.weaviate.io/semitechnologies/weaviate:1.22.4",
        Port = 8080,
        StartupPriority = 2,
        HealthTimeout = TimeSpan.FromMinutes(2),
        Environment = new Dictionary<string, string>
        {
            ["QUERY_DEFAULTS_LIMIT"] = "25",
            ["AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED"] = "true",
            ["PERSISTENCE_DATA_PATH"] = "/var/lib/weaviate",
            ["DEFAULT_VECTORIZER_MODULE"] = "none",
            ["CLUSTER_HOSTNAME"] = "node1"
        }
    };
}