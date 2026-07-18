using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.SqliteVec;

internal sealed class SqliteVecHealthContributor(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<SqliteVecOptions> options,
    SqliteVecAdapterFactory factory,
    IVectorAdapterParticipation participation)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = SqliteVecRoute.Resolve(configuration, sources, options.Value, factory, source);
        SqliteVecRoute.PrepareFileSystem(route.ConnectionString);

        await using var connection = new SqliteConnection(route.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        Vec0Native.Load(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
