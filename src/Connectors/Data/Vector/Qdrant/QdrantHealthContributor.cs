using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Vector.Connector.Qdrant;

public sealed class QdrantHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<QdrantOptions> options,
    ILogger<QdrantHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:qdrant";
    public bool IsCritical => true;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var response = await http.GetAsync("/readyz", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)response.StatusCode}: {body}", null, null);
            }

            logger?.LogDebug("Qdrant health: {Body}", body);
            return new HealthReport(Name, HealthState.Healthy, "qdrant reachable", null, null);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Qdrant health check failed");
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}
