using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Koan.Data.Sqlite;

internal sealed class SqliteConnectionFactory : Koan.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
           || provider.Contains("sqlite", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new SqliteConnection(connectionString);
}