using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Connector.Qdrant;

public sealed class QdrantHealthContributor(
    IHttpClientFactory httpFactory,
    IOptions<QdrantOptions> options,
    IVectorAdapterParticipation participation,
    ILogger<QdrantHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase("qdrant", participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
        http.BaseAddress = new Uri(options.Value.Endpoint);
        var response = await http.GetAsync("/readyz", ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
        }

        KoanLog.HealthDebug(logger, Infrastructure.Constants.Logging.Health, "healthy",
            ("status", (int)response.StatusCode));
    }
}
