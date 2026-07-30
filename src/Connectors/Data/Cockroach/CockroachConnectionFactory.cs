using System.Data.Common;
using Koan.Data.Core.Configuration;
using Npgsql;

namespace Koan.Data.Connector.Cockroach;

internal sealed class CockroachConnectionFactory : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) =>
        string.Equals(provider, Infrastructure.Constants.Provider, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, Infrastructure.Constants.Alias, StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString) => new NpgsqlConnection(connectionString);
}
