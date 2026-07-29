using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.OpenSearch;

public sealed class OpenSearchHealthContributor(
    IHttpClientFactory http,
    IOptions<OpenSearchOptions> options,
    OpenSearchVectorAdapterFactory factory,
    IVectorAdapterParticipation participation,
    ILogger<OpenSearchHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        using var client = new OpenSearchClient(http, factory.ResolveRoute(source), options.Value);
        await client.Probe(ct).ConfigureAwait(false);
        KoanLog.HealthDebug(logger, Infrastructure.Constants.HealthLog, "healthy", ("source", source));
    }
}
