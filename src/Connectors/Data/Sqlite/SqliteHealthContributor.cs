using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Connector.Sqlite.Runtime;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly SqliteAdapterFactory _factory;
    private readonly SqliteConnectionManager _connections;

    public SqliteHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider,
        SqliteConnectionManager connections)
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
        await using var connection = _connections.Create(route.Options.ConnectionString, source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
