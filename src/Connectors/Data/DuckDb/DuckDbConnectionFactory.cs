using System.Data.Common;
using Koan.Data.Connector.DuckDb.Runtime;
using Koan.Data.Core.Configuration;

namespace Koan.Data.Connector.DuckDb;

internal sealed class DuckDbConnectionFactory(
    IServiceProvider services,
    DuckDbAdapterFactory factory,
    DuckDbConnections connections) : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) => DuckDbAdapterFactory.HandlesProvider(provider);
    public DbConnection Create(string connectionString) => connections.Create(connectionString, "Direct");
    public DbConnection Create(string connectionString, string source) => connections.Create(connectionString, source);
    public string? ResolveConnectionString(string source) => factory.ResolveRoute(services, source).ConnectionString;
}
