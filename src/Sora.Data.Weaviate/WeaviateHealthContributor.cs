using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Core;

namespace Sora.Data.Weaviate;

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
                return new HealthReport(Name, HealthState.Healthy);
            }
            else
            {
                logger?.LogDebug("Weaviate health: not ready ({Status})", (int)resp.StatusCode);
                return new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}
