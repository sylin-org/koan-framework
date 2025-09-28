using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Core.Orchestration;

/// <summary>
/// Backward-compatible implementation that delegates to the new adapter-based coordinator.
/// Maintains existing interface while using autonomous adapter pattern internally.
/// ZERO provider-specific knowledge - pure delegation only.
/// </summary>
public sealed class OrchestrationAwareServiceDiscoveryV2 : IOrchestrationAwareServiceDiscovery
{
    private readonly IServiceDiscoveryCoordinator _coordinator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrchestrationAwareServiceDiscoveryV2> _logger;

    public OrchestrationMode CurrentMode => KoanEnv.OrchestrationMode;

    public OrchestrationAwareServiceDiscoveryV2(
        IServiceDiscoveryCoordinator coordinator,
        IConfiguration configuration,
        ILogger<OrchestrationAwareServiceDiscoveryV2> logger)
    {
        _coordinator = coordinator;
        _configuration = configuration;
        _logger = logger;
    }

    public string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints)
    {
        // Legacy method - delegate to new adapter system
        var context = new DiscoveryContext
        {
            OrchestrationMode = CurrentMode,
            Configuration = _configuration,
            RequireHealthValidation = false, // Legacy method didn't require health checks
            Parameters = ExtractParametersFromHints(hints)
        };

        try
        {
            // "Adapter, discover yourself"
            var result = _coordinator.DiscoverServiceAsync(serviceName, context).GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                _logger.LogDebug("Resolved {ServiceName} connection via adapter: {ServiceUrl}", serviceName, result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                _logger.LogWarning("Adapter discovery failed for {ServiceName}, falling back to hints: {Error}", serviceName, result.ErrorMessage);
                return FallbackToHints(hints);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception during adapter discovery for {ServiceName}, falling back to hints", serviceName);
            return FallbackToHints(hints);
        }
    }

    public async Task<ServiceDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        ServiceDiscoveryOptions discovery,
        CancellationToken cancellationToken = default)
    {
        // Convert legacy options to new context
        var context = new DiscoveryContext
        {
            OrchestrationMode = CurrentMode,
            Configuration = _configuration,
            RequireHealthValidation = discovery.HealthCheck?.Required ?? true,
            HealthCheckTimeout = discovery.HealthCheck?.Timeout ?? TimeSpan.FromSeconds(5),
            Parameters = ExtractParametersFromOptions(discovery)
        };

        try
        {
            // "Adapter, discover yourself"
            var adapterResult = await _coordinator.DiscoverServiceAsync(serviceName, context, cancellationToken);

            if (adapterResult.IsSuccessful)
            {
                // Convert internal result to legacy result format
                return adapterResult.ToServiceDiscoveryResult();
            }
            else
            {
                _logger.LogWarning("Adapter discovery failed for {ServiceName}: {Error}", serviceName, adapterResult.ErrorMessage);
                throw new InvalidOperationException($"Service discovery failed for '{serviceName}': {adapterResult.ErrorMessage}");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Exception during adapter discovery for {ServiceName}", serviceName);
            throw new InvalidOperationException($"Service discovery failed for '{serviceName}': {ex.Message}", ex);
        }
    }

    /// <summary>Extract parameters from legacy hints (no provider-specific knowledge)</summary>
    private IDictionary<string, object>? ExtractParametersFromHints(OrchestrationConnectionHints hints)
    {
        var parameters = new Dictionary<string, object>();

        // Extract generic parameters that adapters can use
        if (!string.IsNullOrWhiteSpace(hints.ServiceName))
            parameters["serviceName"] = hints.ServiceName;

        if (hints.DefaultPort > 0)
            parameters["defaultPort"] = hints.DefaultPort;

        return parameters.Count > 0 ? parameters : null;
    }

    /// <summary>Extract parameters from legacy options (no provider-specific knowledge)</summary>
    private IDictionary<string, object>? ExtractParametersFromOptions(ServiceDiscoveryOptions options)
    {
        var parameters = new Dictionary<string, object>();

        // Pass through additional candidates for adapters to use
        if (options.AdditionalCandidates?.Length > 0)
            parameters["additionalCandidates"] = options.AdditionalCandidates;

        // Pass through explicit config sections for adapters to check
        if (options.ExplicitConfigurationSections?.Length > 0)
            parameters["explicitConfigSections"] = options.ExplicitConfigurationSections;

        return parameters.Count > 0 ? parameters : null;
    }

    /// <summary>Fallback to legacy hints when adapter discovery fails</summary>
    private string FallbackToHints(OrchestrationConnectionHints hints)
    {
        // Use existing hint resolution logic as fallback
        return CurrentMode switch
        {
            OrchestrationMode.DockerCompose or OrchestrationMode.Kubernetes => hints.DockerCompose ?? hints.SelfOrchestrated ?? "localhost",
            OrchestrationMode.AspireAppHost => hints.AspireManaged ?? hints.DockerCompose ?? "localhost",
            OrchestrationMode.SelfOrchestrating => hints.SelfOrchestrated ?? "localhost",
            _ => hints.External ?? hints.SelfOrchestrated ?? "localhost"
        };
    }
}