using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Sora.Data.Sqlite;

internal sealed class SqliteConnectionFactory : Sora.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
           || provider.Contains("sqlite", StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString)
        => new SqliteConnection(connectionString);
}