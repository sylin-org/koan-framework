using System.Data.Common;
using Koan.Data.Core.Configuration;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer;

internal sealed class SqlServerConnectionFactory : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) =>
        string.Equals(provider, Infrastructure.Constants.Provider, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, Infrastructure.Constants.Service, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString) => new SqlConnection(connectionString);
}
