namespace Koan.ServiceMesh.Abstractions;

/// <summary>
/// Represents a discovered service instance.
/// </summary>
public class ServiceInstance
{
    /// <summary>
    /// Unique instance identifier (generated per container/process).
    /// </summary>
    public string InstanceId { get; set; } = "";

    /// <summary>
    /// Service type identifier (e.g., "translation").
    /// </summary>
    public string ServiceId { get; set; } = "";

    /// <summary>
    /// HTTP endpoint for this instance (e.g., "http://172.18.0.3:8080").
    /// </summary>
    public string HttpEndpoint { get; set; } = "";

    /// <summary>
    /// Service-specific multicast endpoint (optional, if EnableServiceChannel = true).
    /// Format: "239.255.42.10:42010"
    /// </summary>
    public string? ServiceChannelEndpoint { get; set; }

    /// <summary>
    /// Capabilities provided by this service (e.g., ["translate", "detect-language"]).
    /// </summary>
    public string[] Capabilities { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Last time heartbeat was received from this instance.
    /// </summary>
    public DateTime LastSeen { get; set; }

    /// <summary>
    /// Current health status of this instance.
    /// </summary>
    public ServiceInstanceStatus Status { get; set; }

    /// <summary>
    /// Number of active connections/requests to this instance.
    /// Used for least-connections load balancing.
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Average response time for requests to this instance.
    /// Used for health-aware load balancing.
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// Deployment mode (in-process or container).
    /// </summary>
    public ServiceDeploymentMode DeploymentMode { get; set; }

    /// <summary>
    /// Container ID if deployed as container.
    /// </summary>
    public string? ContainerId { get; set; }
}

/// <summary>
/// Service instance health status.
/// </summary>
public enum ServiceInstanceStatus
{
    /// <summary>
    /// Instance is healthy and receiving traffic.
    /// </summary>
    Healthy,

    /// <summary>
    /// Instance is degraded but still functional.
    /// </summary>
    Degraded,

    /// <summary>
    /// Instance is unhealthy and should not receive traffic.
    /// </summary>
    Unhealthy
}

/// <summary>
/// Service deployment mode.
/// </summary>
public enum ServiceDeploymentMode
{
    /// <summary>
    /// Service running in same process (NuGet package).
    /// </summary>
    InProcess,

    /// <summary>
    /// Service running in separate container.
    /// </summary>
    Container
}
