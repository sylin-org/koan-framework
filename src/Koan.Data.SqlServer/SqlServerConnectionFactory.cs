using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace Koan.Data.SqlServer;

internal sealed class SqlServerConnectionFactory : Koan.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("mssql", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new SqlConnection(connectionString);
}