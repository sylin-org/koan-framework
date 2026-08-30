using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Chroma;

public sealed class ChromaHealthContributor(
    IHttpClientFactory http,
    IOptions<ChromaOptions> options,
    ChromaVectorAdapterFactory factory,
    IVectorAdapterParticipation participation,
    ILogger<ChromaHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        using var client = new ChromaClient(http, factory.ResolveRoute(source), options.Value);
        await client.Probe(ct).ConfigureAwait(false);
        KoanLog.HealthDebug(logger, Infrastructure.Constants.HealthLog, "healthy", ("source", source));
    }
}
