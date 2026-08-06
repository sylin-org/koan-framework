using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Npgsql;

namespace Koan.Data.Connector.Cockroach;

internal sealed class CockroachHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly CockroachAdapterFactory _factory;

    public CockroachHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Infrastructure.Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(Infrastructure.Constants.Provider) as CockroachAdapterFactory
            ?? throw new InvalidOperationException("The CockroachDB adapter is absent from the data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);
        await using var connection = new NpgsqlConnection(route.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
