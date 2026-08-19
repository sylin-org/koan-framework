using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using MySqlConnector;

namespace Koan.Data.Connector.MySql;

internal sealed class MySqlHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly MySqlAdapterFactory _factory;

    public MySqlHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Infrastructure.Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(Infrastructure.Constants.Provider) as MySqlAdapterFactory
            ?? throw new InvalidOperationException("The MySQL adapter is absent from the data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);
        await using var connection = new MySqlConnection(route.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT 1", connection);
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
