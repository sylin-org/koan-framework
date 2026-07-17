using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.Logging;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Vector.Connector.Weaviate;

public sealed class WeaviateHealthContributor(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options, ILogger<WeaviateHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:weaviate";
    public bool IsCritical => true;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient("weaviate");
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var readyPath = "/.well-known/ready";
            var full = new Uri(http.BaseAddress!, readyPath).AbsoluteUri;
            KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "probe",
                ("url", full));
            var resp = await http.GetAsync(readyPath, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var fallbackPath = "/v1/.well-known/ready";
                var fallbackUrl = new Uri(http.BaseAddress!, fallbackPath).AbsoluteUri;
                KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "fallback",
                    ("url", fallbackUrl),
                    ("priorStatus", (int)resp.StatusCode));
                resp = await http.GetAsync(fallbackPath, ct);
            }
            if (resp.IsSuccessStatusCode)
            {
                KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "healthy",
                    ("status", (int)resp.StatusCode));
                return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, null);
            }
            else
            {
                KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "unhealthy",
                    ("status", (int)resp.StatusCode));
                return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}", null, null);
            }
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}

