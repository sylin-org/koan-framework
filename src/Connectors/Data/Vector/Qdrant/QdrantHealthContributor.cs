using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Qdrant;

public sealed class QdrantHealthContributor(
    IHttpClientFactory http,
    IOptions<QdrantOptions> options,
    QdrantVectorAdapterFactory factory,
    IVectorAdapterParticipation participation,
    ILogger<QdrantHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        using var client = new QdrantClient(http, factory.ResolveRoute(source), options.Value);
        await client.Probe(ct).ConfigureAwait(false);
        KoanLog.HealthDebug(logger, Infrastructure.Constants.HealthLog, "healthy", ("source", source));
    }
}
