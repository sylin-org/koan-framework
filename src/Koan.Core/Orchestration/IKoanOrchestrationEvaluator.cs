using Microsoft.Extensions.Configuration;

namespace Koan.Core.Orchestration;

/// <summary>
/// Interface for adapter-driven orchestration evaluation.
/// Each adapter implements this to determine if it requires orchestration support.
/// </summary>
public interface IKoanOrchestrationEvaluator
{
    /// <summary>
    /// Service name used for container management and discovery
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Priority for dependency startup ordering (lower numbers start first)
    /// </summary>
    int StartupPriority { get; }

    /// <summary>
    /// Evaluates whether this adapter requires orchestration support
    /// based on the current configuration and environment.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="context">Current orchestration context</param>
    /// <returns>Orchestration decision with action and dependency details</returns>
    Task<OrchestrationDecision> EvaluateAsync(IConfiguration configuration, OrchestrationContext context);
}

/// <summary>
/// Context information for orchestration evaluation
/// </summary>
public record OrchestrationContext
{
    /// <summary>Current orchestration mode</summary>
    public required OrchestrationMode Mode { get; init; }

    /// <summary>Session ID for container grouping</summary>
    public required string SessionId { get; init; }

    /// <summary>Application ID for container labeling</summary>
    public required string AppId { get; init; }

    /// <summary>Application instance ID for uniqueness</summary>
    public required string AppInstance { get; init; }

    /// <summary>Environment variables for container injection</summary>
    public required Dictionary<string, string> EnvironmentVariables { get; init; }
}

/// <summary>
/// Result of orchestration evaluation
/// </summary>
public record OrchestrationDecision
{
    /// <summary>Action to take for this service</summary>
    public required OrchestrationAction Action { get; init; }

    /// <summary>Dependency descriptor if provisioning is required</summary>
    public DependencyDescriptor? DependencyDescriptor { get; init; }

    /// <summary>Reason for the decision</summary>
    public required string Reason { get; init; }

    /// <summary>Service-specific metadata</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Orchestration action to take for a service
/// </summary>
public enum OrchestrationAction
{
    /// <summary>Service is disabled - no action needed</summary>
    Skip,

    /// <summary>Use existing external service - no provisioning</summary>
    UseExternal,

    /// <summary>Provision container for this service</summary>
    ProvisionContainer,

    /// <summary>Expected to be managed by external orchestrator (Aspire, etc.)</summary>
    ManagedExternally
}

/// <summary>
/// Configuration for orchestration behavior
/// </summary>
public record OrchestrationOptions
{
    /// <summary>Global provisioning mode</summary>
    public ProvisioningMode Global { get; init; } = ProvisioningMode.Auto;

    /// <summary>Service-specific provisioning overrides</summary>
    public Dictionary<string, ProvisioningMode> Services { get; init; } = new();

    /// <summary>Gets the effective provisioning mode for a service</summary>
    public ProvisioningMode GetEffectiveMode(string serviceName)
    {
        return Services.TryGetValue(serviceName, out var serviceMode) ? serviceMode : Global;
    }
}

/// <summary>
/// Provisioning mode for orchestration
/// </summary>
public enum ProvisioningMode
{
    /// <summary>Automatic detection - provision if no suitable host service found</summary>
    Auto,

    /// <summary>Always provision containers regardless of host availability</summary>
    Always,

    /// <summary>Never provision - must use external services</summary>
    Never,

    /// <summary>Service is disabled completely</summary>
    Disabled
}

/// <summary>
/// Descriptor for a dependency that needs to be provisioned
/// </summary>
public record DependencyDescriptor
{
    /// <summary>Service name for container and discovery</summary>
    public required string Name { get; init; }

    /// <summary>Container image to use</summary>
    public required string Image { get; init; }

    /// <summary>Primary port</summary>
    public required int Port { get; init; }

    /// <summary>Additional ports if needed</summary>
    public Dictionary<int, int>? Ports { get; init; }

    /// <summary>Startup priority (lower starts first)</summary>
    public int StartupPriority { get; init; } = 100;

    /// <summary>Health check timeout</summary>
    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Health check command</summary>
    public string? HealthCheckCommand { get; init; }

    /// <summary>Environment variables for the container</summary>
    public Dictionary<string, string> Environment { get; init; } = new();

    /// <summary>Volume mappings</summary>
    public List<string> Volumes { get; init; } = new();
}