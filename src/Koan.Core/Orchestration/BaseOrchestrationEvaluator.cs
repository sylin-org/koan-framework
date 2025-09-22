using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Linq;

namespace Koan.Core.Orchestration;

/// <summary>
/// Base implementation for orchestration evaluators with common smart host detection
/// and configuration handling logic.
/// </summary>
public abstract class BaseOrchestrationEvaluator : IKoanOrchestrationEvaluator
{
    protected readonly ILogger? Logger;

    protected BaseOrchestrationEvaluator(ILogger? logger = null)
    {
        Logger = logger;
    }

    public abstract string ServiceName { get; }
    public abstract int StartupPriority { get; }

    public virtual async Task<OrchestrationDecision> EvaluateAsync(IConfiguration configuration, OrchestrationContext context)
    {
        Logger?.LogDebug("[{ServiceName}] Starting orchestration evaluation", ServiceName);

        // Get orchestration options
        var orchestrationOptions = GetOrchestrationOptions(configuration);
        var effectiveMode = orchestrationOptions.GetEffectiveMode(ServiceName);

        Logger?.LogDebug("[{ServiceName}] Effective provisioning mode: {Mode}", ServiceName, effectiveMode);

        // Check if service is disabled
        if (effectiveMode == ProvisioningMode.Disabled || !IsServiceEnabled(configuration))
        {
            Logger?.LogInformation("[{ServiceName}] Service is disabled", ServiceName);
            return new OrchestrationDecision
            {
                Action = OrchestrationAction.Skip,
                Reason = "Service is disabled in configuration"
            };
        }

        // Check for explicit external configuration
        if (HasExplicitConfiguration(configuration))
        {
            Logger?.LogInformation("[{ServiceName}] Using explicit external configuration", ServiceName);
            return new OrchestrationDecision
            {
                Action = OrchestrationAction.UseExternal,
                Reason = "Explicit external configuration found"
            };
        }

        // Handle Aspire managed scenarios
        if (context.Mode == OrchestrationMode.AspireAppHost)
        {
            Logger?.LogInformation("[{ServiceName}] Service expected to be managed by Aspire", ServiceName);
            return new OrchestrationDecision
            {
                Action = OrchestrationAction.ManagedExternally,
                Reason = "Service is managed by Aspire AppHost"
            };
        }

        // Evaluate based on provisioning mode
        switch (effectiveMode)
        {
            case ProvisioningMode.Always:
                Logger?.LogInformation("[{ServiceName}] Always provision mode - creating container", ServiceName);
                return new OrchestrationDecision
                {
                    Action = OrchestrationAction.ProvisionContainer,
                    DependencyDescriptor = await CreateDependencyDescriptorAsync(configuration, context),
                    Reason = "Always provision mode enabled"
                };

            case ProvisioningMode.Never:
                Logger?.LogInformation("[{ServiceName}] Never provision mode - must use external", ServiceName);
                return new OrchestrationDecision
                {
                    Action = OrchestrationAction.UseExternal,
                    Reason = "Never provision mode - external service required"
                };

            case ProvisioningMode.Auto:
                return await EvaluateAutoMode(configuration, context);

            default:
                Logger?.LogWarning("[{ServiceName}] Unknown provisioning mode: {Mode}", ServiceName, effectiveMode);
                return new OrchestrationDecision
                {
                    Action = OrchestrationAction.UseExternal,
                    Reason = $"Unknown provisioning mode: {effectiveMode}"
                };
        }
    }

    protected virtual async Task<OrchestrationDecision> EvaluateAutoMode(IConfiguration configuration, OrchestrationContext context)
    {
        Logger?.LogDebug("[{ServiceName}] Evaluating auto mode with smart host detection", ServiceName);

        // Perform smart host detection
        var hostDetectionResult = await PerformSmartHostDetection(configuration);

        if (hostDetectionResult.IsHostAvailable)
        {
            // Check if credentials match
            if (await ValidateHostCredentials(configuration, hostDetectionResult))
            {
                Logger?.LogInformation("[{ServiceName}] Host service available with matching credentials", ServiceName);
                return new OrchestrationDecision
                {
                    Action = OrchestrationAction.UseExternal,
                    Reason = "Host service available with valid credentials",
                    Metadata = new Dictionary<string, object>
                    {
                        ["HostEndpoint"] = hostDetectionResult.HostEndpoint!,
                        ["DetectionMethod"] = hostDetectionResult.DetectionMethod
                    }
                };
            }
            else
            {
                Logger?.LogInformation("[{ServiceName}] Host service available but credentials mismatch - provisioning container", ServiceName);
                return new OrchestrationDecision
                {
                    Action = OrchestrationAction.ProvisionContainer,
                    DependencyDescriptor = await CreateDependencyDescriptorAsync(configuration, context),
                    Reason = "Host service available but credentials don't match"
                };
            }
        }
        else
        {
            Logger?.LogInformation("[{ServiceName}] No host service detected - provisioning container", ServiceName);
            return new OrchestrationDecision
            {
                Action = OrchestrationAction.ProvisionContainer,
                DependencyDescriptor = await CreateDependencyDescriptorAsync(configuration, context),
                Reason = "No suitable host service detected"
            };
        }
    }

    protected virtual async Task<HostDetectionResult> PerformSmartHostDetection(IConfiguration configuration)
    {
        var defaultPort = GetDefaultPort();

        // Try localhost first
        if (await IsPortOpen("localhost", defaultPort))
        {
            Logger?.LogDebug("[{ServiceName}] Host service detected on localhost:{Port}", ServiceName, defaultPort);
            return new HostDetectionResult
            {
                IsHostAvailable = true,
                HostEndpoint = $"localhost:{defaultPort}",
                DetectionMethod = "localhost-port-scan"
            };
        }

        // Try 127.0.0.1
        if (await IsPortOpen("127.0.0.1", defaultPort))
        {
            Logger?.LogDebug("[{ServiceName}] Host service detected on 127.0.0.1:{Port}", ServiceName, defaultPort);
            return new HostDetectionResult
            {
                IsHostAvailable = true,
                HostEndpoint = $"127.0.0.1:{defaultPort}",
                DetectionMethod = "localhost-ip-port-scan"
            };
        }

        // Check additional host candidates
        var additionalHosts = GetAdditionalHostCandidates(configuration);
        foreach (var host in additionalHosts)
        {
            if (await IsPortOpen(host, defaultPort))
            {
                Logger?.LogDebug("[{ServiceName}] Host service detected on {Host}:{Port}", ServiceName, host, defaultPort);
                return new HostDetectionResult
                {
                    IsHostAvailable = true,
                    HostEndpoint = $"{host}:{defaultPort}",
                    DetectionMethod = "configured-host-port-scan"
                };
            }
        }

        Logger?.LogDebug("[{ServiceName}] No host service detected", ServiceName);
        return new HostDetectionResult
        {
            IsHostAvailable = false,
            DetectionMethod = "port-scan-negative"
        };
    }

    protected virtual async Task<bool> IsPortOpen(string host, int port, int timeoutMs = 2000)
    {
        try
        {
            using var client = new TcpClient();

            // Set a reasonable timeout and try to connect
            using var cancellationTokenSource = new CancellationTokenSource(timeoutMs);

            try
            {
                await client.ConnectAsync(host, port, cancellationTokenSource.Token);
                return client.Connected;
            }
            catch (OperationCanceledException)
            {
                // Timeout occurred
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    protected virtual OrchestrationOptions GetOrchestrationOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Koan:Orchestration");
        var options = new OrchestrationOptions();

        // Get global mode
        var globalModeString = section.GetValue<string>("Global");
        if (Enum.TryParse<ProvisioningMode>(globalModeString, true, out var globalMode))
        {
            options = options with { Global = globalMode };
        }

        // Get service-specific overrides
        var servicesSection = section.GetSection("Services");
        var serviceOverrides = new Dictionary<string, ProvisioningMode>();

        foreach (var child in servicesSection.GetChildren())
        {
            if (Enum.TryParse<ProvisioningMode>(child.Value, true, out var serviceMode))
            {
                serviceOverrides[child.Key] = serviceMode;
            }
            else if (!string.IsNullOrEmpty(child.Value))
            {
                Logger?.LogWarning("[{ServiceName}] Invalid provisioning mode '{Value}' for service '{Key}'", ServiceName, child.Value, child.Key);
            }
        }

        var result = options with { Services = serviceOverrides };

        return result;
    }

    // Abstract methods that derived classes must implement
    protected abstract bool IsServiceEnabled(IConfiguration configuration);
    protected abstract bool HasExplicitConfiguration(IConfiguration configuration);
    protected abstract int GetDefaultPort();
    protected abstract Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult);
    protected abstract Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context);
    protected abstract string[] GetAdditionalHostCandidates(IConfiguration configuration);
}

/// <summary>
/// Result of smart host detection
/// </summary>
public record HostDetectionResult
{
    /// <summary>Whether a host service is available</summary>
    public required bool IsHostAvailable { get; init; }

    /// <summary>Host endpoint if available</summary>
    public string? HostEndpoint { get; init; }

    /// <summary>How the host was detected</summary>
    public required string DetectionMethod { get; init; }

    /// <summary>Additional metadata about detection</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}