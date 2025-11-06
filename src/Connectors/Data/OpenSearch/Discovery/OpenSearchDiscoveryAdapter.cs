using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.OpenSearch.Discovery;

/// <summary>
/// OpenSearch autonomous discovery adapter.
/// Contains ALL OpenSearch-specific knowledge - core orchestration knows nothing about OpenSearch.
/// Reads own KoanServiceAttribute and handles OpenSearch-specific health checks.
/// </summary>
internal sealed class OpenSearchDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "opensearch";
    public override string[] Aliases => new[] { "open-search", "os", "search" };

    public OpenSearchDiscoveryAdapter(IConfiguration configuration, ILogger<OpenSearchDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>OpenSearch adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(OpenSearchVectorAdapterFactory);

    /// <summary>OpenSearch-specific health validation using cluster health API</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            // OpenSearch cluster health endpoint (same as ElasticSearch)
            var healthUrl = new Uri(new Uri(serviceUrl), "/_cluster/health").ToString();
            var response = await httpClient.GetAsync(healthUrl, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("OpenSearch health check passed using /_cluster/health for {Url}", serviceUrl);
                return true;
            }

            // Fallback to basic root endpoint check
            var rootResponse = await httpClient.GetAsync(serviceUrl, cts.Token);
            var isHealthy = rootResponse.IsSuccessStatusCode;

            _logger.LogDebug("OpenSearch health check {Result} using root endpoint for {Url}",
                isHealthy ? "passed" : "failed", serviceUrl);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("OpenSearch health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>OpenSearch adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check OpenSearch-specific configuration paths
        return _configuration.GetConnectionString("OpenSearch") ??
               _configuration.GetConnectionString("Opensearch") ??
               _configuration["Koan:Data:OpenSearch:ConnectionString"] ??
               _configuration["Koan:Data:OpenSearch:Endpoint"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>OpenSearch-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var openSearchUrls = Environment.GetEnvironmentVariable("OPENSEARCH_URLS") ??
                            Environment.GetEnvironmentVariable("OPEN_SEARCH_URLS");

        if (string.IsNullOrWhiteSpace(openSearchUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return openSearchUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(url => new DiscoveryCandidate(url.Trim(), "environment-opensearch-urls", 0));
    }

    /// <summary>OpenSearch-specific connection string parameter application</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        return BuildOpenSearchConnectionString(baseUrl, parameters);
    }

    /// <summary>OpenSearch-specific connection string construction</summary>
    private string BuildOpenSearchConnectionString(string baseUrl, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Handle opensearch:// URL format or plain http URLs
            if (baseUrl.StartsWith("opensearch://", StringComparison.OrdinalIgnoreCase))
            {
                // Convert opensearch:// to http://
                baseUrl = baseUrl.Replace("opensearch://", "http://");
            }

            // For OpenSearch, we primarily need the base URL
            // Authentication and other parameters can be handled separately
            var uri = new Uri(baseUrl);
            var cleanUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

            // Apply parameters if provided (for future extensibility)
            if (parameters != null)
            {
                // OpenSearch authentication typically handled via headers or configuration
                // Connection string format is typically just the URL
            }

            return cleanUrl;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to build OpenSearch connection string from {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parsing fails
        }
    }

    /// <summary>OpenSearch adapter handles Aspire service discovery for OpenSearch</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific OpenSearch service discovery
        return _configuration["services:opensearch:default:0"] ??
               _configuration["services:open-search:default:0"];
    }
}
