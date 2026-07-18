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

    /// <summary>Milvus-specific health validation using the same REST endpoint as the adapter.</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(context.HealthCheckTimeout);

        var endpoint = new Uri(serviceUrl);
        var healthUrl = new Uri(endpoint, "/v2/health").ToString();
        var response = await httpClient.GetAsync(healthUrl, cts.Token);

        if (response.IsSuccessStatusCode) return true;

        using var tcpClient = new System.Net.Sockets.TcpClient();
        await tcpClient.ConnectAsync(endpoint.Host, endpoint.Port, cts.Token);
        return tcpClient.Connected;
    }

    /// <summary>Milvus adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check Milvus-specific configuration paths
        return _configuration.GetConnectionString("Milvus") ??
               _configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString] ??
               _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint];
    }

    /// <summary>Milvus-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var milvusUrls = Environment.GetEnvironmentVariable("MILVUS_URLS") ??
                        Environment.GetEnvironmentVariable("MILVUS_ENDPOINTS");

        if (string.IsNullOrWhiteSpace(milvusUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return milvusUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(url => new DiscoveryCandidate(url.Trim(), "environment-milvus-urls", DiscoveryCandidatePriority.Environment));
    }

    /// <summary>The provider consumes the discovered REST endpoint directly.</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
        => baseUrl;

    /// <summary>Milvus adapter handles Aspire service discovery for Milvus</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific Milvus service discovery
        return _configuration["services:milvus:default:0"] ??
               _configuration["services:milvus-db:default:0"];
    }
}
