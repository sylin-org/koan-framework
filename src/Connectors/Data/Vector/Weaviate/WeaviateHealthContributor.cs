using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Connector.Weaviate;

public sealed class WeaviateHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<WeaviateOptions> options,
    IVectorAdapterParticipation participation,
    ILogger<WeaviateHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase("weaviate", participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("weaviate");
        http.BaseAddress = new Uri(options.Value.Endpoint);
        var readyPath = "/.well-known/ready";
        var full = new Uri(http.BaseAddress!, readyPath).AbsoluteUri;
        KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "probe",
            ("url", full));
        var response = await http.GetAsync(readyPath, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var fallbackPath = "/v1/.well-known/ready";
            var fallbackUrl = new Uri(http.BaseAddress!, fallbackPath).AbsoluteUri;
            KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "fallback",
                ("url", fallbackUrl),
                ("priorStatus", (int)response.StatusCode));
            response = await http.GetAsync(fallbackPath, ct).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        }

        KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "healthy",
            ("status", (int)response.StatusCode));
    }
}

