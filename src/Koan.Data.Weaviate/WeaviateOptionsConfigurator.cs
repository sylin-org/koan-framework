using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.Weaviate;

/// <summary>
/// Orchestration-aware Weaviate configuration using centralized service discovery.
/// Replaces custom auto-detection with unified Koan orchestration patterns.
/// </summary>
internal sealed class WeaviateOptionsConfigurator(IConfiguration config, ILogger<WeaviateOptionsConfigurator> logger) : IConfigureOptions<WeaviateOptions>
{
    public void Configure(WeaviateOptions options)
    {
        logger.LogInformation("Weaviate Orchestration-Aware Configuration Started");
        logger.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        logger.LogInformation("Initial options - Endpoint: '{Endpoint}'", options.Endpoint);

        // Use centralized orchestration-aware service discovery
        var serviceDiscovery = new OrchestrationAwareServiceDiscovery(config, null);

        // Check for explicit endpoint configuration first
        var explicitEndpoint = Configuration.ReadFirst(config, "",
            "Koan:Data:Weaviate:Endpoint",
            "Koan:Data:Weaviate:BaseUrl",
            "ConnectionStrings:weaviate",
            "ConnectionStrings:Weaviate");

        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            logger.LogInformation("Using explicit endpoint from configuration");
            options.Endpoint = explicitEndpoint;
        }
        else if (string.Equals(options.Endpoint?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.Endpoint) ||
                 IsDefault(options.Endpoint))
        {
            logger.LogInformation("Auto-detection mode - using orchestration-aware service discovery");
            options.Endpoint = ResolveOrchestrationAwareEndpoint(serviceDiscovery, logger);
        }
        else
        {
            logger.LogInformation("Using pre-configured endpoint");
        }

        // Final endpoint logging
        logger.LogInformation("Final Weaviate Configuration");
        logger.LogInformation("Endpoint: {Endpoint}", options.Endpoint);
        logger.LogInformation("Weaviate Orchestration-Aware Configuration Complete");
    }

    private string ResolveOrchestrationAwareEndpoint(
        IOrchestrationAwareServiceDiscovery serviceDiscovery,
        ILogger logger)
    {
        try
        {
            // Check if auto-detection is explicitly disabled
            if (IsAutoDetectionDisabled())
            {
                logger.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "http://localhost:8080"; // Use standard Weaviate port
            }

            // Create service discovery options with Weaviate-specific health checking
            var discoveryOptions = ServiceDiscoveryExtensions.ForWeaviate();

            // Add Weaviate-specific health checking with custom validation
            discoveryOptions = discoveryOptions with
            {
                HealthCheck = new HealthCheckOptions
                {
                    CustomHealthCheck = async (serviceUrl, ct) =>
                    {
                        return await TryWeaviateHealthCheck(serviceUrl, TimeSpan.FromMilliseconds(500), ct);
                    },
                    Timeout = TimeSpan.FromMilliseconds(500),
                    Required = !KoanEnv.IsProduction // Less strict in production
                },
                AdditionalCandidates = GetAdditionalCandidatesFromEnvironment()
            };

            // Use centralized service discovery
            var discoveryTask = serviceDiscovery.DiscoverServiceAsync("weaviate", discoveryOptions);
            var result = discoveryTask.GetAwaiter().GetResult();

            logger.LogInformation("Weaviate discovered via {Method}: {ServiceUrl}",
                result.DiscoveryMethod, result.ServiceUrl);

            if (!result.IsHealthy && discoveryOptions.HealthCheck?.Required == true)
            {
                logger.LogWarning("Discovered Weaviate service failed health check but proceeding anyway");
            }

            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration-aware Weaviate discovery, falling back to localhost");
            return "http://localhost:8080";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Configuration.Read(config, "Koan:Data:Weaviate:DisableAutoDetection", false)
               || Configuration.Read(config, "Koan_DATA_WEAVIATE_DISABLE_AUTO_DETECTION", false);
    }

    private string[] GetAdditionalCandidatesFromEnvironment()
    {
        var candidates = new List<string>();

        // Legacy environment variable support for backward compatibility
        var envList = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(envList))
        {
            candidates.AddRange(envList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        return candidates.ToArray();
    }

    private static bool IsDefault(string endpoint)
        => endpoint.TrimEnd('/') == "http://localhost:8085";

    private static async Task<bool> TryWeaviateHealthCheck(string serviceUrl, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = timeout };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            // Prefer well-known readiness endpoint (no version prefix in some Weaviate releases)
            var readyUrl = new Uri(new Uri(serviceUrl), "/.well-known/ready").ToString();
            var response = await httpClient.GetAsync(readyUrl, cts.Token);
            if (response.IsSuccessStatusCode) return true;

            // Fallback for deployments exposing readiness under /v1
            var readyV1Url = new Uri(new Uri(serviceUrl), "/v1/.well-known/ready").ToString();
            var responseV1 = await httpClient.GetAsync(readyV1Url, cts.Token);
            if (responseV1.IsSuccessStatusCode) return true;

            // Final fallback to schema endpoint
            var schemaUrl = new Uri(new Uri(serviceUrl), "/v1/schema").ToString();
            var schemaResponse = await httpClient.GetAsync(schemaUrl, cts.Token);
            return schemaResponse.IsSuccessStatusCode || (int)schemaResponse.StatusCode == 405; // 405 on POST-only clusters still implies reachability
        }
        catch
        {
            return false;
        }
    }
}