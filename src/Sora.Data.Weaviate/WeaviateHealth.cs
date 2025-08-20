using Microsoft.Extensions.Options;
using Sora.Core;

namespace Sora.Data.Weaviate;

public sealed class WeaviateHealthContributor(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options) : IHealthContributor
{
    public string Name => "data:weaviate";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient("weaviate");
            http.BaseAddress = new Uri(options.Value.Endpoint);
            var resp = await http.GetAsync("/.well-known/ready", ct);
            return resp.IsSuccessStatusCode
                ? new HealthReport(Name, HealthState.Healthy)
                : new HealthReport(Name, HealthState.Unhealthy, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}
