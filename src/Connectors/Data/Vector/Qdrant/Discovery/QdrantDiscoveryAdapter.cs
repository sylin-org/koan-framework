using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Vector.Connector.Qdrant.Discovery;

internal sealed class QdrantDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<QdrantDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => Infrastructure.Constants.Provider.Aliases;

    protected override Type GetFactoryType() => typeof(QdrantVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = QdrantRoute.NormalizeEndpoint(serviceUrl);
        using var response = await client.GetAsync(new Uri(endpoint, Infrastructure.Constants.ReadyPath.TrimStart('/')),
            cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint] ??
        _configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString] ??
        _configuration.GetConnectionString("Qdrant");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var value = Environment.GetEnvironmentVariable("QDRANT_URLS") ??
                    Environment.GetEnvironmentVariable("QDRANT_URL") ??
                    Environment.GetEnvironmentVariable("QDRANT_ENDPOINT");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => new DiscoveryCandidate(
                    endpoint, "environment-qdrant", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) => baseUrl;

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:qdrant:default:0"] ?? _configuration["services:qdrant-db:default:0"];
}
