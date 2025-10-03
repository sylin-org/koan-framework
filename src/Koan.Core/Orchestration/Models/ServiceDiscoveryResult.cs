namespace Koan.Core.Orchestration;

/// <summary>Adapter discovery result - extends existing ServiceDiscoveryResult for new adapter system</summary>
public sealed record AdapterDiscoveryResult
{
    public string ServiceName { get; init; } = "";
    public string ServiceUrl { get; init; } = "";
    public bool IsSuccessful { get; init; }
    public bool IsHealthy { get; init; }
    public string DiscoveryMethod { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public IDictionary<string, object>? Metadata { get; init; }
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;

    // Factory methods
    public static AdapterDiscoveryResult Success(string serviceName, string serviceUrl, string method, bool isHealthy = true) =>
        new() { ServiceName = serviceName, ServiceUrl = serviceUrl, DiscoveryMethod = method, IsSuccessful = true, IsHealthy = isHealthy };

    public static AdapterDiscoveryResult Failed(string serviceName, string error) =>
        new() { ServiceName = serviceName, IsSuccessful = false, ErrorMessage = error };

    public static AdapterDiscoveryResult NoAdapter(string serviceName) =>
        Failed(serviceName, $"No discovery adapter registered for service '{serviceName}'");

    // Convert to existing ServiceDiscoveryResult for backward compatibility
    public ServiceDiscoveryResult ToServiceDiscoveryResult()
    {
        if (!IsSuccessful)
        {
            throw new InvalidOperationException($"Cannot convert failed result to ServiceDiscoveryResult: {ErrorMessage}");
        }

        return new ServiceDiscoveryResult
        {
            ServiceUrl = ServiceUrl,
            DiscoveryMethod = ConvertDiscoveryMethod(DiscoveryMethod),
            IsHealthy = IsHealthy,
            Metadata = Metadata as Dictionary<string, object>
        };
    }

    private ServiceDiscoveryMethod ConvertDiscoveryMethod(string method) => method switch
    {
        "aspire-discovery" => ServiceDiscoveryMethod.AspireServiceDiscovery,
        "explicit-config" => ServiceDiscoveryMethod.ExplicitConfiguration,
        "environment-urls" => ServiceDiscoveryMethod.EnvironmentVariable,
        "container-dns" or "self-orchestrated" or "localhost" => ServiceDiscoveryMethod.OrchestrationAwareDiscovery,
        _ => ServiceDiscoveryMethod.DefaultFallback
    };
}