using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Observability.Health;

namespace Koan.Data.Vector.Connector.Milvus;

public sealed class MilvusHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<MilvusOptions> options,
    ILogger<MilvusHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:milvus";
    public bool IsCritical => true;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var path = "/v2/health";
            var response = await http.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)response.StatusCode}: {body}", null, null);
            }

            KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "healthy",
                ("status", (int)response.StatusCode));
            return new HealthReport(Name, HealthState.Healthy, "milvus reachable", null, null);
        }
        catch (Exception ex)
        {
            KoanLog.HealthWarning(logger, Infrastructure.Constants.Logging.Health, "failed",
                ("error", ex));
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}

