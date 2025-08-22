using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Sora.Data.SqlServer;

internal sealed class SqlServerConnectionFactory : Sora.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("mssql", StringComparison.OrdinalIgnoreCase)
           || provider.Equals("microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new SqlConnection(connectionString);
}