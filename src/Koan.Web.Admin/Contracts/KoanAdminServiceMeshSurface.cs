namespace Koan.Web.Admin.Contracts;

/// <summary>
/// Runtime state snapshot of the Koan Service Mesh.
/// Complements static service definitions from provenance.
/// </summary>
public sealed record KoanAdminServiceMeshSurface(
    bool Enabled,
    DateTimeOffset CapturedAt,
    string OrchestratorChannel,
    int TotalServicesCount,
    int TotalInstancesCount,
    int HealthyInstancesCount,
    int DegradedInstancesCount,
    int UnhealthyInstancesCount,
    MeshConfiguration? Configuration,
    IReadOnlyList<KoanAdminServiceSurface> Services
)
{
    public static readonly KoanAdminServiceMeshSurface Empty = new(
        Enabled: false,
        CapturedAt: DateTimeOffset.UtcNow,
        OrchestratorChannel: string.Empty,
        TotalServicesCount: 0,
        TotalInstancesCount: 0,
        HealthyInstancesCount: 0,
        DegradedInstancesCount: 0,
        UnhealthyInstancesCount: 0,
        Configuration: null,
        Services: Array.Empty<KoanAdminServiceSurface>()
    );
}

public sealed record KoanAdminServiceSurface(
    string ServiceId,
    string DisplayName,
    string? Description,
    string[] Capabilities,
    int InstanceCount,
    ServiceHealthDistribution Health,
    LoadBalancingInfo LoadBalancing,
    TimeSpan? MinResponseTime,
    TimeSpan? MaxResponseTime,
    TimeSpan? AvgResponseTime,
    ServiceConfiguration? Configuration,
    CapacityMetrics Capacity,
    IReadOnlyList<KoanAdminServiceInstanceSurface> Instances
);

public sealed record ServiceHealthDistribution(
    int Healthy,
    int Degraded,
    int Unhealthy,
    int HealthyPercent,
    int DegradedPercent,
    int UnhealthyPercent
);

public sealed record LoadBalancingInfo(
    string Policy,  // "RoundRobin", "LeastConnections", "HealthAware", "Random"
    string? Description  // Human-readable policy description
);

public sealed record KoanAdminServiceInstanceSurface(
    string InstanceId,
    string HttpEndpoint,
    string? ServiceChannelEndpoint,  // e.g., "239.255.42.10:42010" (optional)
    string Status,  // "Healthy", "Degraded", "Unhealthy"
    DateTime LastSeen,
    string TimeSinceLastSeen,  // Formatted like "5 seconds ago"
    int ActiveConnections,
    string AverageResponseTime,  // Formatted like "245ms"
    string DeploymentMode,  // "InProcess", "Container"
    string? ContainerId,
    string[] Capabilities
);

/// <summary>
/// Mesh-wide configuration settings.
/// </summary>
public sealed record MeshConfiguration(
    string OrchestratorMulticastGroup,
    int OrchestratorMulticastPort,
    string HeartbeatInterval,  // Formatted like "10s"
    string StaleThreshold,  // Formatted like "30s"
    string? SelfInstanceId  // The instance ID of the current node (if hosting services)
);

/// <summary>
/// Service-specific configuration settings.
/// </summary>
public sealed record ServiceConfiguration(
    int Port,
    string HealthEndpoint,
    string ManifestEndpoint,
    bool EnableServiceChannel,
    string? ServiceMulticastGroup,
    int? ServiceMulticastPort,
    string? ContainerImage,
    string? DefaultTag
);

/// <summary>
/// Service capacity and load metrics.
/// </summary>
public sealed record CapacityMetrics(
    int TotalCapacity,  // Total instances
    int AvailableCapacity,  // Healthy instances
    int CapacityUtilizationPercent,  // Percentage of capacity in use (based on connections)
    int TotalConnections,  // Total active connections across all instances
    double AverageLoadPerInstance  // Average connections per instance
);
