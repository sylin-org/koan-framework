using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Vector.Connector.Chroma.Discovery;

internal sealed class ChromaDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<ChromaDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => Infrastructure.Constants.Provider.Aliases;

    protected override Type GetFactoryType() => typeof(ChromaVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = ChromaRoute.NormalizeEndpoint(serviceUrl);
        using var response = await client.GetAsync(new Uri(endpoint, Infrastructure.Constants.HeartbeatPath.TrimStart('/')),
            cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint] ??
        _configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString] ??
        _configuration.GetConnectionString("Chroma");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var value = Environment.GetEnvironmentVariable("CHROMA_URLS") ??
                    Environment.GetEnvironmentVariable("CHROMA_URL") ??
                    Environment.GetEnvironmentVariable("CHROMA_ENDPOINT");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => new DiscoveryCandidate(
                    endpoint, "environment-chroma", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) => baseUrl;

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:chroma:default:0"] ?? _configuration["services:chromadb:default:0"];
}
