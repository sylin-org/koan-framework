using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Core.Orchestration;

/// <summary>
/// Enhanced orchestration-aware service discovery that unifies connection string resolution
/// and service URL discovery with intelligent health checking and fallback logic.
/// </summary>
public sealed class OrchestrationAwareServiceDiscovery : IOrchestrationAwareServiceDiscovery
{
    private readonly IConfiguration? _configuration;
    private readonly ILogger<OrchestrationAwareServiceDiscovery>? _logger;
    private readonly IOrchestrationAwareConnectionResolver _connectionResolver;

    public OrchestrationMode CurrentMode => KoanEnv.OrchestrationMode;

    public OrchestrationAwareServiceDiscovery(
        IConfiguration? configuration = null,
        ILogger<OrchestrationAwareServiceDiscovery>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionResolver = new OrchestrationAwareConnectionResolver(configuration);
    }

    public string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints)
    {
        return _connectionResolver.ResolveConnectionString(serviceName, hints);
    }

    public async Task<ServiceDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        ServiceDiscoveryOptions discovery,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Starting service discovery for {ServiceName}", serviceName);

        // Phase 1: Check for Aspire service discovery (highest priority)
        var aspireResult = await TryAspireDiscoveryAsync(serviceName, discovery, cancellationToken);
        if (aspireResult != null) return aspireResult;

        // Phase 2: Check for explicit configuration
        var explicitResult = await TryExplicitConfigurationAsync(serviceName, discovery, cancellationToken);
        if (explicitResult != null) return explicitResult;

        // Phase 3: Check environment variables
        var envResult = await TryEnvironmentVariablesAsync(serviceName, discovery, cancellationToken);
        if (envResult != null) return envResult;

        // Phase 4: Orchestration-aware discovery with health checking
        var orchestrationResult = await TryOrchestrationAwareDiscoveryAsync(serviceName, discovery, cancellationToken);
        if (orchestrationResult != null) return orchestrationResult;

        // Phase 5: Final fallback
        return CreateFallbackResult(serviceName, discovery);
    }

    private async Task<ServiceDiscoveryResult?> TryAspireDiscoveryAsync(
        string serviceName,
        ServiceDiscoveryOptions discovery,
        CancellationToken cancellationToken)
    {
        if (_configuration == null || CurrentMode != OrchestrationMode.AspireAppHost)
            return null;

        var aspireUrl = _configuration.GetConnectionString(serviceName);
        if (string.IsNullOrEmpty(aspireUrl)) return null;

        _logger?.LogDebug("Found Aspire service discovery for {ServiceName}: {Url}", serviceName, aspireUrl);

        var isHealthy = await CheckServiceHealthAsync(aspireUrl, discovery.HealthCheck, cancellationToken);

        return new ServiceDiscoveryResult
        {
            ServiceUrl = aspireUrl,
            DiscoveryMethod = ServiceDiscoveryMethod.AspireServiceDiscovery,
            IsHealthy = isHealthy,
            Metadata = new Dictionary<string, object>
            {
                ["source"] = "aspire",
                ["orchestrationMode"] = CurrentMode.ToString()
            }
        };
    }

    private async Task<ServiceDiscoveryResult?> TryExplicitConfigurationAsync(
        string serviceName,
        ServiceDiscoveryOptions discovery,
        CancellationToken cancellationToken)
    {
        if (_configuration == null || discovery.ExplicitConfigurationSections == null)
            return null;

        foreach (var section in discovery.ExplicitConfigurationSections)
        {
            var explicitUrl = Configuration.ReadFirst(_configuration, "",
                $"{section}:{serviceName}:BaseUrl",
                $"{section}:{serviceName}:Url",
                $"{section}:BaseUrl",
                $"ConnectionStrings:{serviceName}");

            if (!string.IsNullOrWhiteSpace(explicitUrl))
            {
                _logger?.LogDebug("Found explicit configuration for {ServiceName}: {Url}", serviceName, explicitUrl);

                var isHealthy = await CheckServiceHealthAsync(explicitUrl, discovery.HealthCheck, cancellationToken);

                return new ServiceDiscoveryResult
                {
                    ServiceUrl = explicitUrl,
                    DiscoveryMethod = ServiceDiscoveryMethod.ExplicitConfiguration,
                    IsHealthy = isHealthy,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source"] = "explicit_config",
                        ["section"] = section
                    }
                };
            }
        }

        return null;
    }

    private async Task<ServiceDiscoveryResult?> TryEnvironmentVariablesAsync(
        string serviceName,
        ServiceDiscoveryOptions discovery,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        // Check service-specific environment variables
        var serviceEnvVars = new[]
        {
            $"KOAN_{serviceName.ToUpperInvariant()}_URL",
            $"KOAN_{serviceName.ToUpperInvariant()}_BASE_URL",
            $"{serviceName.ToUpperInvariant()}_URL",
            $"{serviceName.ToUpperInvariant()}_BASE_URL"
        };

        foreach (var envVar in serviceEnvVars)
        {
            var envValue = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                candidates.AddRange(envValue.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()));
            }
        }

        // Add additional candidates from discovery options
        if (discovery.AdditionalCandidates != null)
        {
            candidates.AddRange(discovery.AdditionalCandidates);
        }

        foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            _logger?.LogDebug("Testing environment candidate for {ServiceName}: {Url}", serviceName, candidate);

            var isHealthy = await CheckServiceHealthAsync(candidate, discovery.HealthCheck, cancellationToken);
            if (isHealthy || discovery.HealthCheck?.Required != true)
            {
                return new ServiceDiscoveryResult
                {
                    ServiceUrl = candidate,
                    DiscoveryMethod = ServiceDiscoveryMethod.EnvironmentVariable,
                    IsHealthy = isHealthy,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source"] = "environment_variable"
                    }
                };
            }
        }

        return null;
    }

    private async Task<ServiceDiscoveryResult?> TryOrchestrationAwareDiscoveryAsync(
        string serviceName,
        ServiceDiscoveryOptions discovery,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("Starting orchestration-aware discovery for {ServiceName} in {Mode} mode",
            serviceName, CurrentMode);

        var candidates = GenerateOrchestrationCandidates(serviceName, discovery.UrlHints);

        foreach (var candidate in candidates)
        {
            _logger?.LogDebug("Testing orchestration candidate for {ServiceName}: {Url}", serviceName, candidate);

            var isHealthy = await CheckServiceHealthAsync(candidate, discovery.HealthCheck, cancellationToken);
            if (isHealthy || discovery.HealthCheck?.Required != true)
            {
                _logger?.LogInformation("Discovered {ServiceName} via orchestration-aware discovery: {Url}",
                    serviceName, candidate);

                return new ServiceDiscoveryResult
                {
                    ServiceUrl = candidate,
                    DiscoveryMethod = ServiceDiscoveryMethod.OrchestrationAwareDiscovery,
                    IsHealthy = isHealthy,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source"] = "orchestration_aware",
                        ["orchestrationMode"] = CurrentMode.ToString()
                    }
                };
            }
        }

        return null;
    }

    private IEnumerable<string> GenerateOrchestrationCandidates(string serviceName, OrchestrationConnectionHints hints)
    {
        var candidates = new List<string>();

        switch (CurrentMode)
        {
            case OrchestrationMode.SelfOrchestrating:
                if (!string.IsNullOrWhiteSpace(hints.SelfOrchestrated))
                    candidates.Add(hints.SelfOrchestrated);
                else
                    candidates.Add($"http://localhost:{hints.DefaultPort}");
                break;

            case OrchestrationMode.DockerCompose:
                if (!string.IsNullOrWhiteSpace(hints.DockerCompose))
                    candidates.Add(hints.DockerCompose);
                else
                    candidates.Add($"http://{hints.ServiceName ?? serviceName}:{hints.DefaultPort}");
                break;

            case OrchestrationMode.Kubernetes:
                if (!string.IsNullOrWhiteSpace(hints.Kubernetes))
                    candidates.Add(hints.Kubernetes);
                else
                    candidates.Add($"http://{hints.ServiceName ?? serviceName}.default.svc.cluster.local:{hints.DefaultPort}");
                break;

            case OrchestrationMode.Standalone:
                // In standalone mode, we expect explicit configuration
                // But provide some common defaults as last resort
                candidates.Add($"http://localhost:{hints.DefaultPort}");
                break;

            case OrchestrationMode.AspireAppHost:
                // Aspire should provide via service discovery, but fallback to localhost
                candidates.Add($"http://localhost:{hints.DefaultPort}");
                break;
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c));
    }

    private ServiceDiscoveryResult CreateFallbackResult(string serviceName, ServiceDiscoveryOptions discovery)
    {
        var fallbackUrl = discovery.UrlHints.SelfOrchestrated ??
                         $"http://localhost:{discovery.UrlHints.DefaultPort}";

        _logger?.LogWarning("No healthy service found for {ServiceName}, using fallback: {Url}",
            serviceName, fallbackUrl);

        return new ServiceDiscoveryResult
        {
            ServiceUrl = fallbackUrl,
            DiscoveryMethod = ServiceDiscoveryMethod.DefaultFallback,
            IsHealthy = false,
            Metadata = new Dictionary<string, object>
            {
                ["source"] = "fallback",
                ["warning"] = "Service discovery failed, using fallback configuration"
            }
        };
    }

    private async Task<bool> CheckServiceHealthAsync(
        string serviceUrl,
        HealthCheckOptions? healthCheck,
        CancellationToken cancellationToken)
    {
        if (healthCheck == null) return true;

        try
        {
            // Use custom health check if provided
            if (healthCheck.CustomHealthCheck != null)
            {
                return await healthCheck.CustomHealthCheck(serviceUrl, cancellationToken);
            }

            // Default HTTP health check
            if (!string.IsNullOrWhiteSpace(healthCheck.HealthCheckPath))
            {
                return await DefaultHttpHealthCheckAsync(serviceUrl, healthCheck, cancellationToken);
            }

            // If no health check path specified, assume service is healthy
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Health check failed for {ServiceUrl}", serviceUrl);
            return false;
        }
    }

    private async Task<bool> DefaultHttpHealthCheckAsync(
        string serviceUrl,
        HealthCheckOptions healthCheck,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = healthCheck.Timeout };
            var healthUrl = CombineUrl(serviceUrl, healthCheck.HealthCheckPath!);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(healthCheck.Timeout);

            var response = await httpClient.GetAsync(healthUrl, cts.Token);
            var isHealthy = response.IsSuccessStatusCode;

            _logger?.LogDebug("Health check for {ServiceUrl}: {Status}",
                serviceUrl, isHealthy ? "healthy" : $"unhealthy ({response.StatusCode})");

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "HTTP health check failed for {ServiceUrl}", serviceUrl);
            return false;
        }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var uri = new Uri(baseUrl.TrimEnd('/'));
        return new Uri(uri, path.TrimStart('/')).ToString();
    }
}