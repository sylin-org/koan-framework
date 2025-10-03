using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Vector.Connector.Weaviate;

public sealed class WeaviateHealthContributor(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options, ILogger<WeaviateHealthContributor>? logger = null) : IHealthContributor
{
    public string Name => "data:weaviate";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient("weaviate");
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var readyPath = "/.well-known/ready";
            var full = new Uri(http.BaseAddress!, readyPath).AbsoluteUri;
            logger?.LogDebug("Weaviate health: GET {Url}", full);
            var resp = await http.GetAsync(readyPath, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var fallbackPath = "/v1/.well-known/ready";
                var fallbackUrl = new Uri(http.BaseAddress!, fallbackPath).AbsoluteUri;
                logger?.LogDebug("Weaviate health: fallback GET {Url} (prior {Status})", fallbackUrl, (int)resp.StatusCode);
                resp = await http.GetAsync(fallbackPath, ct);
            }
            if (resp.IsSuccessStatusCode)
            {
                logger?.LogDebug("Weaviate health: ready ({Status})", (int)resp.StatusCode);
                return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, null);
            }
            else
            {
                logger?.LogDebug("Weaviate health: not ready ({Status})", (int)resp.StatusCode);
                return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}", null, null);
            }
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}

