using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using WeaviateItems = Koan.Data.Vector.Connector.Weaviate.Infrastructure.WeaviateProvenanceItems;

namespace Koan.Data.Vector.Connector.Weaviate.Discovery;

/// <summary>
/// Weaviate autonomous discovery adapter.
/// Contains ALL Weaviate-specific knowledge - core orchestration knows nothing about Weaviate.
/// Reads own KoanServiceAttribute and handles Weaviate-specific health checks.
/// </summary>
internal sealed class WeaviateDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "weaviate";
    public override string[] Aliases => new[] { "weaviate-db", "vector-db" };

    public WeaviateDiscoveryAdapter(IConfiguration configuration, ILogger<WeaviateDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>Weaviate adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(WeaviateVectorAdapterFactory);

    /// <summary>Weaviate-specific health validation using HTTP health checks</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            // Prefer well-known readiness endpoint (no version prefix in some Weaviate releases)
            var readyUrl = new Uri(new Uri(serviceUrl), "/.well-known/ready").ToString();
            var response = await httpClient.GetAsync(readyUrl, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Weaviate health check passed using /.well-known/ready for {Url}", serviceUrl);
                return true;
            }

            // Fallback for deployments exposing readiness under /v1
            var readyV1Url = new Uri(new Uri(serviceUrl), "/v1/.well-known/ready").ToString();
            var responseV1 = await httpClient.GetAsync(readyV1Url, cts.Token);
            if (responseV1.IsSuccessStatusCode)
            {
                _logger.LogDebug("Weaviate health check passed using /v1/.well-known/ready for {Url}", serviceUrl);
                return true;
            }

            // Final fallback to meta endpoint (standard Weaviate health endpoint)
            var metaUrl = new Uri(new Uri(serviceUrl), "/v1/meta").ToString();
            var metaResponse = await httpClient.GetAsync(metaUrl, cts.Token);
            var isHealthy = metaResponse.IsSuccessStatusCode;

            _logger.LogDebug("Weaviate health check {Result} using /v1/meta for {Url}",
                isHealthy ? "passed" : "failed", serviceUrl);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Weaviate health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Weaviate adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check Weaviate-specific configuration paths
        foreach (var key in WeaviateItems.ConnectionStringKeys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        foreach (var key in WeaviateItems.EndpointKeys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Weaviate-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var weaviateUrls = Environment.GetEnvironmentVariable("WEAVIATE_URLS") ??
                          Environment.GetEnvironmentVariable("WEAVIATE_ENDPOINTS");

        if (string.IsNullOrWhiteSpace(weaviateUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return weaviateUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(url => new DiscoveryCandidate(url.Trim(), "environment-weaviate-urls", 0));
    }

    /// <summary>Weaviate adapter handles Aspire service discovery for Weaviate</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific Weaviate service discovery
        return _configuration["services:weaviate:default:0"] ??
               _configuration["services:weaviate-db:default:0"];
    }
}
