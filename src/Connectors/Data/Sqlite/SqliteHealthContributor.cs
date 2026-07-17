using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Sqlite;

/// <summary>Reports readiness for the SQLite sources that actually participate in this application.</summary>
internal sealed class SqliteHealthContributor : DataAdapterHealthContributorBase
{
    private const string ProviderName = "sqlite";
    private readonly IConfiguration _configuration;
    private readonly DataSourceRegistry _sourceRegistry;
    private readonly IOptions<SqliteOptions> _options;
    private readonly SqliteConnectionLifecycle _connections;
    private readonly IAdapterFactory _sourceOwner;

    public SqliteHealthContributor(
        IServiceProvider services,
        IConfiguration configuration,
        DataSourceRegistry sourceRegistry,
        IDataDiagnostics diagnostics,
        IOptions<SqliteOptions> options,
        SqliteConnectionLifecycle connections,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(ProviderName, services, diagnostics, defaultProvider)
    {
        _configuration = configuration;
        _sourceRegistry = sourceRegistry;
        _options = options;
        _connections = connections;
        _sourceOwner = providers.Find(ProviderName)
            ?? throw new InvalidOperationException("The SQLite provider is absent from the host Data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            _configuration,
            _sourceRegistry,
            ProviderName,
            source,
            _options.Value.ConnectionString,
            _sourceOwner);

        await using var connection = _connections.Create(connectionString, source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
