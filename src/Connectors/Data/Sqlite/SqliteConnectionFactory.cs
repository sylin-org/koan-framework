using System.Data.Common;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Core.Configuration;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteConnectionFactory(
    IServiceProvider services,
    SqliteAdapterFactory factory,
    SqliteConnections connections) : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) => SqliteAdapterFactory.HandlesProvider(provider);
    public DbConnection Create(string connectionString) => connections.Create(connectionString, "Direct");
    public DbConnection Create(string connectionString, string source) => connections.Create(connectionString, source);
    public string? ResolveConnectionString(string source) => factory.ResolveRoute(services, source).ConnectionString;
}
