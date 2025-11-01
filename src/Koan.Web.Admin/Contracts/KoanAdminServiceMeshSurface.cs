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
    IReadOnlyList<KoanAdminServiceInstanceSurface> Instances
);

public sealed record ServiceHealthDistribution(
    int Healthy,
    int Degraded,
    int Unhealthy
);

public sealed record LoadBalancingInfo(
    string Policy,  // "RoundRobin", "LeastConnections", "HealthAware", "Random"
    string? Description  // Human-readable policy description
);

public sealed record KoanAdminServiceInstanceSurface(
    string InstanceId,
    string HttpEndpoint,
    string Status,  // "Healthy", "Degraded", "Unhealthy"
    DateTime LastSeen,
    string TimeSinceLastSeen,  // Formatted like "5 seconds ago"
    int ActiveConnections,
    string AverageResponseTime,  // Formatted like "245ms"
    string DeploymentMode,  // "InProcess", "Container"
    string? ContainerId,
    string[] Capabilities
);
