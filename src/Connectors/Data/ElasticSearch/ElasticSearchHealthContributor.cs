using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Connector.ElasticSearch;

public sealed class ElasticSearchHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<ElasticSearchOptions> options,
    ILogger<ElasticSearchHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:elasticsearch";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var path = "/_cluster/health";
            var full = new Uri(http.BaseAddress!, path).AbsoluteUri;
            logger?.LogDebug("Elasticsearch health: GET {Url}", full);
            var resp = await http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var txt = await resp.Content.ReadAsStringAsync(ct);
                return new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}: {txt}", null, null);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            logger?.LogDebug("Elasticsearch health: {Body}", body);
            return new HealthReport(Name, HealthState.Healthy, "cluster reachable", null, null);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Elasticsearch health check failed");
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}

