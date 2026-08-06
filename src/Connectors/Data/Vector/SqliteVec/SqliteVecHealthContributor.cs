using Koan.Data.Vector.Abstractions;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Vector.Connector.SqliteVec;

internal sealed class SqliteVecHealthContributor(
    SqliteVecAdapterFactory factory,
    SqliteVecNative native,
    IVectorAdapterParticipation participation)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = factory.ResolveRoute(source);
        var connectionString = ExistingReadConnection(route.ConnectionString);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        native.Load(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static string ExistingReadConnection(string connectionString)
    {
        var value = new SqliteConnectionStringBuilder(connectionString);
        if (value.Mode == SqliteOpenMode.Memory ||
            string.Equals(value.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            return value.ToString();
        var path = System.IO.Path.GetFullPath(value.DataSource);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                "The selected SqliteVec source file does not exist. Save through a Managed source or provision an External source first.");
        value.Mode = SqliteOpenMode.ReadOnly;
        return value.ToString();
    }
}
