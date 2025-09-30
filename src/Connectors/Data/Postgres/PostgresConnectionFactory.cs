using Npgsql;
using System.Data.Common;

namespace Koan.Data.Connector.Postgres;

internal sealed class PostgresConnectionFactory : Koan.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("npgsql", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new NpgsqlConnection(connectionString);
}
