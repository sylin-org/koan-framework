using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.OpenSearch;

public sealed class OpenSearchHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<OpenSearchOptions> options,
    ILogger<OpenSearchHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:opensearch";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var path = "/_cluster/health";
            var full = new Uri(http.BaseAddress!, path).AbsoluteUri;
            logger?.LogDebug("OpenSearch health: GET {Url}", full);
            var resp = await http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var txt = await resp.Content.ReadAsStringAsync(ct);
                return new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}: {txt}", null, null);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            logger?.LogDebug("OpenSearch health: {Body}", body);
            return new HealthReport(Name, HealthState.Healthy, "cluster reachable", null, null);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "OpenSearch health check failed");
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}
