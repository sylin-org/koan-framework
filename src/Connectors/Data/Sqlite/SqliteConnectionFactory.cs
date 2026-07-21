using Microsoft.Data.Sqlite;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteConnectionFactory(
    SqliteConnectionLifecycle connections,
    IConfiguration configuration,
    DataSourceRegistry sourceRegistry,
    IOptions<SqliteOptions> options,
    DataProviderCatalog providers) : Koan.Data.Core.Configuration.IDataProviderConnectionFactory
{
    public bool CanHandle(string provider)
        => SqliteAdapterFactory.HandlesProvider(provider);

    public string ResolveConnectionString(string source)
        => AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            sourceRegistry,
            "sqlite",
            source,
            options.Value.ConnectionString,
            providers.Find("sqlite")
                ?? throw new InvalidOperationException("The SQLite provider is absent from the host Data catalog."));

    public DbConnection Create(string connectionString)
        => connections.Create(connectionString);

    public DbConnection Create(string connectionString, string source)
        => connections.Create(connectionString, source);
}
