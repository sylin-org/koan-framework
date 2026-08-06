using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly SqliteAdapterFactory _factory;
    private readonly SqliteConnections _connections;

    public SqliteHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider,
        SqliteConnections connections)
        : base(Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _connections = connections;
        _factory = providers.Find(Constants.Provider) as SqliteAdapterFactory
            ?? throw new InvalidOperationException("The SQLite provider is absent from the host Data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);
        await using var connection = _connections.Create(route.ConnectionString, route.Source, nonCreating: true);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
