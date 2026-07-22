using Npgsql;
using System.Data.Common;

namespace Koan.Data.Connector.Cockroach;

internal sealed class CockroachConnectionFactory : Koan.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals(Infrastructure.Constants.Provider.Name, StringComparison.OrdinalIgnoreCase)
           || provider.Equals(Infrastructure.Constants.Provider.Alias, StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new NpgsqlConnection(connectionString);
}
