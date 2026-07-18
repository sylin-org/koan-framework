using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Vector.Connector.Qdrant.Discovery;

/// <summary>
/// Qdrant autonomous discovery adapter. Holds all Qdrant-specific knowledge so the core
/// orchestration layer doesn't need to know anything about Qdrant. Health check hits
/// Qdrant's <c>/readyz</c> endpoint on the REST port.
/// </summary>
internal sealed class QdrantDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "qdrant";
    public override string[] Aliases => new[] { "qdrant-db", "vector-db", "qdrant-vector" };

    public QdrantDiscoveryAdapter(IConfiguration configuration, ILogger<QdrantDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    protected override Type GetFactoryType() => typeof(QdrantVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(context.HealthCheckTimeout);

            // /readyz returns 200 once collections are loaded and the node is accepting queries.
            // /healthz also exists but flips to OK earlier (process alive); /readyz is the right
            // signal for "this Qdrant can actually serve requests."
        var readyUrl = new Uri(new Uri(serviceUrl), "/readyz").ToString();
        var response = await httpClient.GetAsync(readyUrl, cts.Token);
        if (response.IsSuccessStatusCode) return true;

            // Fallback to /healthz for older Qdrant versions or edge configurations.
        var healthUrl = new Uri(new Uri(serviceUrl), "/healthz").ToString();
        var healthResponse = await httpClient.GetAsync(healthUrl, cts.Token);
        if (healthResponse.IsSuccessStatusCode) return true;

            // Final fallback: plain TCP connectivity.
        var uri = new Uri(serviceUrl);
        using var tcpClient = new System.Net.Sockets.TcpClient();
        await tcpClient.ConnectAsync(uri.Host, uri.Port, cts.Token);
        return tcpClient.Connected;
    }

    protected override string? ReadExplicitConfiguration()
    {
        return _configuration.GetConnectionString("Qdrant") ??
               _configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString] ??
               _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint];
    }

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var qdrantUrls = Environment.GetEnvironmentVariable("QDRANT_URLS") ??
                         Environment.GetEnvironmentVariable("QDRANT_URL") ??
                         Environment.GetEnvironmentVariable("QDRANT_ENDPOINT");

        if (string.IsNullOrWhiteSpace(qdrantUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return qdrantUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(url => new DiscoveryCandidate(url.Trim(), "environment-qdrant-urls", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        // Qdrant connection is just the HTTP endpoint; api-key flows through the header on
        // each request, not the URL. Nothing to encode here — return baseUrl unchanged.
        return baseUrl;
    }

    protected override string? ReadAspireServiceDiscovery()
    {
        return _configuration["services:qdrant:default:0"] ??
               _configuration["services:qdrant-db:default:0"];
    }
}
