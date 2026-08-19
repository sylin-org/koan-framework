using System.Data.Common;
using Koan.Data.Core.Configuration;
using MySqlConnector;

namespace Koan.Data.Connector.MySql;

internal sealed class MySqlConnectionFactory : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) =>
        string.Equals(provider, Infrastructure.Constants.Provider, StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString) =>
        new MySqlConnection(MySqlConnectionStrings.Normalize(connectionString).ConnectionString);
}
