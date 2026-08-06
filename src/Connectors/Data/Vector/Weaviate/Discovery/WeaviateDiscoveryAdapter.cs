using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Vector.Connector.Weaviate.Discovery;

internal sealed class WeaviateDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<WeaviateDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => Infrastructure.Constants.Provider.Aliases;
    protected override Type GetFactoryType() => typeof(WeaviateVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = WeaviateRoute.NormalizeEndpoint(serviceUrl);
        using var response = await client.GetAsync(
            new Uri(endpoint, Infrastructure.Constants.ReadyPath.TrimStart('/')), cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint] ??
        _configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString] ??
        _configuration.GetConnectionString("Weaviate");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var value = Environment.GetEnvironmentVariable("WEAVIATE_URLS") ??
                    Environment.GetEnvironmentVariable("WEAVIATE_URL") ??
                    Environment.GetEnvironmentVariable("WEAVIATE_ENDPOINT");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => new DiscoveryCandidate(
                    endpoint, "environment-weaviate", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) => baseUrl;
    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:weaviate:default:0"] ?? _configuration["services:weaviate-db:default:0"];
}
