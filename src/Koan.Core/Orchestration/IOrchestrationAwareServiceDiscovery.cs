using Microsoft.Extensions.Configuration;

namespace Koan.Core.Orchestration;

/// <summary>
/// Enhanced orchestration-aware service discovery that supports both connection strings (databases)
/// and service URLs (HTTP APIs), with intelligent health checking and fallback logic.
/// </summary>
public interface IOrchestrationAwareServiceDiscovery
{
    /// <summary>
    /// Resolve a connection string for database-style services.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "postgres", "mongodb")</param>
    /// <param name="hints">Orchestration-specific connection patterns</param>
    /// <returns>Connection string appropriate for the current orchestration mode</returns>
    string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints);

    /// <summary>
    /// Discover and resolve a service URL for HTTP-style services with health checking.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "ollama", "weaviate")</param>
    /// <param name="discovery">Service discovery configuration including health checking</param>
    /// <param name="cancellationToken">Cancellation token for health checks</param>
    /// <returns>First healthy service URL found, or configured fallback</returns>
    Task<ServiceDiscoveryResult> DiscoverServiceAsync(string serviceName, ServiceDiscoveryOptions discovery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current orchestration mode being used for service resolution.
    /// </summary>
    OrchestrationMode CurrentMode { get; }
}


/// <summary>
/// Configuration for service discovery with health checking capabilities.
/// </summary>
public record ServiceDiscoveryOptions
{
    /// <summary>Orchestration-aware URL patterns</summary>
    public required OrchestrationConnectionHints UrlHints { get; init; }

    /// <summary>Additional candidate URLs from environment variables or explicit config</summary>
    public string[]? AdditionalCandidates { get; init; }

    /// <summary>Health check configuration</summary>
    public HealthCheckOptions? HealthCheck { get; init; }

    /// <summary>Whether to skip discovery if explicit configuration exists</summary>
    public bool SkipDiscoveryIfConfigured { get; init; } = true;

    /// <summary>Configuration sections to check for explicit service configuration</summary>
    public string[]? ExplicitConfigurationSections { get; init; }
}

/// <summary>
/// Health check configuration for service discovery.
/// </summary>
public record HealthCheckOptions
{
    /// <summary>HTTP path to check for health (e.g., "/api/tags", "/health")</summary>
    public string? HealthCheckPath { get; init; }

    /// <summary>Timeout for individual health checks</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Custom health check function</summary>
    public Func<string, CancellationToken, Task<bool>>? CustomHealthCheck { get; init; }

    /// <summary>Whether health check is required for service registration</summary>
    public bool Required { get; init; } = true;
}

/// <summary>
/// Result of service discovery operation.
/// </summary>
public record ServiceDiscoveryResult
{
    /// <summary>Resolved service URL or connection string</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>How the service was discovered</summary>
    public required ServiceDiscoveryMethod DiscoveryMethod { get; init; }

    /// <summary>Whether the service passed health checks</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Additional metadata about the discovered service</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// How a service was discovered.
/// </summary>
public enum ServiceDiscoveryMethod
{
    /// <summary>From Aspire service discovery</summary>
    AspireServiceDiscovery,
    /// <summary>From explicit configuration</summary>
    ExplicitConfiguration,
    /// <summary>From environment variables</summary>
    EnvironmentVariable,
    /// <summary>From orchestration-aware defaults with health check</summary>
    OrchestrationAwareDiscovery,
    /// <summary>Fallback to default configuration</summary>
    DefaultFallback
}