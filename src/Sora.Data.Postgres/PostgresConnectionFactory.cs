using System.Data.Common;
using Npgsql;

namespace Sora.Data.Postgres;

internal sealed class PostgresConnectionFactory : Sora.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("npgsql", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new NpgsqlConnection(connectionString);
}