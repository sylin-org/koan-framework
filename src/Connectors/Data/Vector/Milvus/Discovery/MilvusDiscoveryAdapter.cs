using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Vector.Connector.Milvus.Discovery;

/// <summary>
/// Milvus autonomous discovery adapter.
/// Contains ALL Milvus-specific knowledge - core orchestration knows nothing about Milvus.
/// Reads own KoanServiceAttribute and handles Milvus-specific health checks.
/// </summary>
internal sealed class MilvusDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "milvus";
    public override string[] Aliases => new[] { "milvus-db", "vector-db", "milvus-vector" };

    public MilvusDiscoveryAdapter(IConfiguration configuration, ILogger<MilvusDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>Milvus adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(MilvusVectorAdapterFactory);

    /// <summary>Milvus-specific health validation using HTTP management API</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            // Try Milvus HTTP management API health endpoint (port 9091 by default)
            var managementUri = ConvertToManagementEndpoint(serviceUrl);
            var healthUrl = new Uri(managementUri, "/healthz").ToString();
            var response = await httpClient.GetAsync(healthUrl, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Milvus health check passed using /healthz for {Url}", serviceUrl);
                return true;
            }

            // Fallback to metrics endpoint on management port
            var metricsUrl = new Uri(managementUri, "/metrics").ToString();
            var metricsResponse = await httpClient.GetAsync(metricsUrl, cts.Token);

            if (metricsResponse.IsSuccessStatusCode)
            {
                _logger.LogDebug("Milvus health check passed using /metrics for {Url}", serviceUrl);
                return true;
            }

            // Final fallback - just try to connect to the gRPC port (basic TCP connectivity)
            // This is less reliable but better than nothing
            var uri = new Uri(serviceUrl);
            using var tcpClient = new System.Net.Sockets.TcpClient();
            await tcpClient.ConnectAsync(uri.Host, uri.Port, cts.Token);
            var isConnected = tcpClient.Connected;

            _logger.LogDebug("Milvus health check {Result} using TCP connectivity for {Url}",
                isConnected ? "passed" : "failed", serviceUrl);
            return isConnected;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Milvus health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Milvus adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check Milvus-specific configuration paths
        return _configuration.GetConnectionString("Milvus") ??
               _configuration["Koan:Data:Milvus:ConnectionString"] ??
               _configuration["Koan:Data:Milvus:Endpoint"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>Milvus-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add Milvus-specific candidates from environment variables (highest priority)
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
                _logger.LogDebug("Milvus adapter: Added container candidate {ContainerUrl} (in container environment)", containerUrl);
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                _logger.LogDebug("Milvus adapter: Added local fallback {LocalUrl}", localhostUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("Milvus adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
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
                _logger.LogDebug("Milvus adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        // Apply Milvus-specific connection parameters if provided
        if (context.Parameters != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i].Url))
                {
                    candidates[i] = candidates[i] with
                    {
                        Url = BuildMilvusConnectionString(candidates[i].Url, context.Parameters)
                    };
                }
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>Milvus-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var milvusUrls = Environment.GetEnvironmentVariable("MILVUS_URLS") ??
                        Environment.GetEnvironmentVariable("MILVUS_ENDPOINTS");

        if (string.IsNullOrWhiteSpace(milvusUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return milvusUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(url => new DiscoveryCandidate(url.Trim(), "environment-milvus-urls", 0));
    }

    /// <summary>Milvus-specific connection string construction</summary>
    private string BuildMilvusConnectionString(string baseUrl, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Handle milvus:// URL format or plain http URLs
            if (baseUrl.StartsWith("milvus://", StringComparison.OrdinalIgnoreCase))
            {
                // Keep milvus:// scheme for gRPC connections
                return baseUrl;
            }

            // For HTTP URLs, assume they point to the gRPC port
            var uri = new Uri(baseUrl);
            var cleanUrl = $"milvus://{uri.Host}:{uri.Port}";

            // Apply parameters if provided (database, authentication, etc.)
            if (parameters != null)
            {
                // Milvus connection parameters can be handled via client configuration
                // The connection string format is typically just the endpoint
            }

            return cleanUrl;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to build Milvus connection string from {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parsing fails
        }
    }

    /// <summary>Convert gRPC endpoint to HTTP management endpoint for health checks</summary>
    private Uri ConvertToManagementEndpoint(string gRpcEndpoint)
    {
        try
        {
            var uri = new Uri(gRpcEndpoint.Replace("milvus://", "http://"));
            // Milvus management API typically runs on port 9091
            var managementPort = 9091;
            return new Uri($"http://{uri.Host}:{managementPort}");
        }
        catch
        {
            // Fallback to standard management port on localhost
            return new Uri("http://localhost:9091");
        }
    }

    /// <summary>Milvus adapter handles Aspire service discovery for Milvus</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific Milvus service discovery
        return _configuration["services:milvus:default:0"] ??
               _configuration["services:milvus-db:default:0"];
    }
}
