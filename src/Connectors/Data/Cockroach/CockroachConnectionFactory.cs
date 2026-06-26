using Npgsql;
using System.Data.Common;

namespace Koan.Data.Connector.Cockroach;

internal sealed class CockroachConnectionFactory : Koan.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("cockroach", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("cockroachdb", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new NpgsqlConnection(connectionString);
}
