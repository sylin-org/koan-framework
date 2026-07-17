using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Observability.Health;

namespace Koan.Data.Connector.ElasticSearch;

public sealed class ElasticSearchHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<ElasticSearchOptions> options,
    ILogger<ElasticSearchHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:elasticsearch";
    public bool IsCritical => true;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var path = "/_cluster/health";
            var full = new Uri(http.BaseAddress!, path).AbsoluteUri;
            KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "probe",
                ("url", full));
            var resp = await http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var txt = await resp.Content.ReadAsStringAsync(ct);
                return new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}: {txt}", null, null);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "healthy",
                ("response", body));
            return new HealthReport(Name, HealthState.Healthy, "cluster reachable", null, null);
        }
        catch (Exception ex)
        {
            KoanLog.HealthWarning(logger, Infrastructure.Constants.Logging.Health, "failed",
                ("error", ex));
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}

