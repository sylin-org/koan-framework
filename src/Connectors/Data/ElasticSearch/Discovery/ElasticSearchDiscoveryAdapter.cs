using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.ElasticSearch.Discovery;

/// <summary>
/// ElasticSearch autonomous discovery adapter.
/// Contains ALL ElasticSearch-specific knowledge - core orchestration knows nothing about ElasticSearch.
/// Reads own KoanServiceAttribute and handles ElasticSearch-specific health checks.
/// </summary>
internal sealed class ElasticSearchDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "elasticsearch";
    public override string[] Aliases => new[] { "elastic", "es", "search" };

    public ElasticSearchDiscoveryAdapter(IConfiguration configuration, ILogger<ElasticSearchDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>ElasticSearch adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(ElasticSearchVectorAdapterFactory);

    /// <summary>ElasticSearch-specific health validation using cluster health API</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            // ElasticSearch cluster health endpoint
            var healthUrl = new Uri(new Uri(serviceUrl), "/_cluster/health").ToString();
            var response = await httpClient.GetAsync(healthUrl, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("ElasticSearch health check passed using /_cluster/health for {Url}", serviceUrl);
                return true;
            }

            // Fallback to basic root endpoint check
            var rootResponse = await httpClient.GetAsync(serviceUrl, cts.Token);
            var isHealthy = rootResponse.IsSuccessStatusCode;

            _logger.LogDebug("ElasticSearch health check {Result} using root endpoint for {Url}",
                isHealthy ? "passed" : "failed", serviceUrl);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ElasticSearch health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>ElasticSearch adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check ElasticSearch-specific configuration paths
        return _configuration.GetConnectionString("ElasticSearch") ??
               _configuration.GetConnectionString("Elasticsearch") ??
               _configuration["Koan:Data:ElasticSearch:ConnectionString"] ??
               _configuration["Koan:Data:ElasticSearch:Endpoint"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>ElasticSearch-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add ElasticSearch-specific candidates from environment variables (highest priority)
        candidates.AddRange(GetEnvironmentCandidates());

        // Add explicit configuration candidates
        var explicitConfig = ReadExplicitConfiguration();
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            candidates.Add(new DiscoveryCandidate(explicitConfig, "explicit-config", 1));
        }

        // Container vs Local detection logic
        if (KoanEnv.InContainer)
        {
            // In container: Try container instance first, then local fallback
            if (!string.IsNullOrWhiteSpace(attribute.Host))
            {
                var containerUrl = $"{attribute.Scheme}://{attribute.Host}:{attribute.EndpointPort}";
                candidates.Add(new DiscoveryCandidate(containerUrl, "container-instance", 2));
                _logger.LogDebug("ElasticSearch adapter: Added container candidate {ContainerUrl} (in container environment)", containerUrl);
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                _logger.LogDebug("ElasticSearch adapter: Added local fallback {LocalUrl}", localhostUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("ElasticSearch adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
            }
        }

        // Special handling for Aspire
        if (context.OrchestrationMode == OrchestrationMode.AspireAppHost)
        {
            var aspireUrl = ReadAspireServiceDiscovery();
            if (!string.IsNullOrWhiteSpace(aspireUrl))
            {
                // Aspire takes priority over container/local discovery
                candidates.Insert(0, new DiscoveryCandidate(aspireUrl, "aspire-discovery", 1));
                _logger.LogDebug("ElasticSearch adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        // Apply ElasticSearch-specific connection parameters if provided
        if (context.Parameters != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i].Url))
                {
                    candidates[i] = candidates[i] with
                    {
                        Url = BuildElasticSearchConnectionString(candidates[i].Url, context.Parameters)
                    };
                }
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>ElasticSearch-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var elasticUrls = Environment.GetEnvironmentVariable("ELASTICSEARCH_URLS") ??
                         Environment.GetEnvironmentVariable("ELASTIC_URLS");

        if (string.IsNullOrWhiteSpace(elasticUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return elasticUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(url => new DiscoveryCandidate(url.Trim(), "environment-elasticsearch-urls", 0));
    }

    /// <summary>ElasticSearch-specific connection string construction</summary>
    private string BuildElasticSearchConnectionString(string baseUrl, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Handle elasticsearch:// URL format or plain http URLs
            if (baseUrl.StartsWith("elasticsearch://", StringComparison.OrdinalIgnoreCase))
            {
                // Convert elasticsearch:// to http://
                baseUrl = baseUrl.Replace("elasticsearch://", "http://");
            }

            // For ElasticSearch, we primarily need the base URL
            // Authentication and other parameters can be handled separately
            var uri = new Uri(baseUrl);
            var cleanUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

            // Apply parameters if provided (for future extensibility)
            if (parameters != null)
            {
                // ElasticSearch authentication typically handled via headers or configuration
                // Connection string format is typically just the URL
            }

            return cleanUrl;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to build ElasticSearch connection string from {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parsing fails
        }
    }

    /// <summary>ElasticSearch adapter handles Aspire service discovery for ElasticSearch</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific ElasticSearch service discovery
        return _configuration["services:elasticsearch:default:0"] ??
               _configuration["services:elastic:default:0"];
    }
}
