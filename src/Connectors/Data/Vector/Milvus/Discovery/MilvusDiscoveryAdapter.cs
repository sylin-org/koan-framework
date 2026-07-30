using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Vector.Connector.Milvus.Discovery;

internal sealed class MilvusDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<MilvusDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => Infrastructure.Constants.Provider.Aliases;
    protected override Type GetFactoryType() => typeof(MilvusVectorAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = MilvusRoute.NormalizeEndpoint(serviceUrl);
        using var response = await client.PostAsync(
            new Uri(endpoint, "v2/vectordb/collections/list"),
            new StringContent("{\"dbName\":\"default\"}", System.Text.Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return false;
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.TryGetProperty("code", out var code) && code.GetInt64() is 0 or 200;
    }

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Infrastructure.Constants.Configuration.Keys.Endpoint] ??
        _configuration[Infrastructure.Constants.Configuration.Keys.LegacyConnectionString] ??
        _configuration.GetConnectionString("Milvus");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var value = Environment.GetEnvironmentVariable("MILVUS_URLS") ??
                    Environment.GetEnvironmentVariable("MILVUS_URL") ??
                    Environment.GetEnvironmentVariable("MILVUS_ENDPOINT");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => new DiscoveryCandidate(
                    endpoint, "environment-milvus", DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) => baseUrl;
    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:milvus:default:0"] ?? _configuration["services:milvus-db:default:0"];
}
