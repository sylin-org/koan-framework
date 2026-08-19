using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Koan.Data.Vector.Connector.PgVector;

internal sealed class PgVectorHealthContributor(
    PgVectorVectorAdapterFactory factory,
    IVectorAdapterParticipation participation,
    ILogger<PgVectorHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = factory.ResolveRoute(source);
        await using var connection = new NpgsqlConnection(route.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT default_version FROM pg_available_extensions WHERE name = 'vector'",
            connection);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string)
            throw new InvalidOperationException(
                "The selected PostgreSQL server does not provide the vector extension. Install pgvector on that server or select another vector adapter.");
        KoanLog.HealthDebug(logger, Infrastructure.Constants.HealthLog, "healthy", ("source", source));
    }
}
