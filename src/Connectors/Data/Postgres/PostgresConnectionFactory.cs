using System.Data.Common;
using Koan.Data.Core.Configuration;
using Npgsql;

namespace Koan.Data.Connector.Postgres;

internal sealed class PostgresConnectionFactory : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) =>
        string.Equals(provider, Infrastructure.Constants.Provider, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "npgsql", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString) => new NpgsqlConnection(connectionString);
}
