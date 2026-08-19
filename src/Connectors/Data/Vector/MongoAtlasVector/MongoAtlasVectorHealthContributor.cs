using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

internal sealed class MongoAtlasVectorHealthContributor(
    MongoAtlasVectorAdapterFactory factory,
    MongoAtlasVectorClientManager clients,
    IVectorAdapterParticipation participation,
    ILogger<MongoAtlasVectorHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        await clients.Probe(factory.ResolveRoute(source), ct).ConfigureAwait(false);
        KoanLog.HealthDebug(logger, Infrastructure.Constants.HealthLog, "healthy", ("source", source));
    }
}
