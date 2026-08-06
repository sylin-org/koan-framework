using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.OpenSearch.Discovery;

internal sealed class OpenSearchDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<OpenSearchDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => ["open-search", "os"];
    protected override Type GetFactoryType() => typeof(OpenSearchVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = OpenSearchRoute.NormalizeEndpoint(serviceUrl);
        using var response = await client.GetAsync(
            new Uri(endpoint, Infrastructure.Constants.HealthPath.TrimStart('/')), cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint] ??
        _configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString] ??
        _configuration.GetConnectionString("OpenSearch");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var value = Environment.GetEnvironmentVariable("OPENSEARCH_URLS") ??
                    Environment.GetEnvironmentVariable("OPEN_SEARCH_URLS");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => new DiscoveryCandidate(
                    endpoint, "environment-opensearch", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) => baseUrl;

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:opensearch:default:0"] ??
        _configuration["services:open-search:default:0"];
}
