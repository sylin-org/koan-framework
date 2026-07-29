using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.ElasticSearch.Discovery;

internal sealed class ElasticSearchDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<ElasticSearchDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => ["elastic", "es"];
    protected override Type GetFactoryType() => typeof(ElasticSearchVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = ElasticSearchRoute.NormalizeEndpoint(serviceUrl);
        using var response = await client.GetAsync(
            new Uri(endpoint, Infrastructure.Constants.HealthPath.TrimStart('/')), cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint] ??
        _configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString] ??
        _configuration.GetConnectionString("ElasticSearch");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var value = Environment.GetEnvironmentVariable("ELASTICSEARCH_URLS") ??
                    Environment.GetEnvironmentVariable("ELASTIC_URLS");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => new DiscoveryCandidate(
                    endpoint, "environment-elasticsearch", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) => baseUrl;

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:elasticsearch:default:0"] ??
        _configuration["services:elastic:default:0"];
}
