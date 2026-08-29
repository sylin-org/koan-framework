using FirebirdSql.Data.FirebirdClient;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Firebird;

internal sealed class FirebirdHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly FirebirdAdapterFactory _factory;

    public FirebirdHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Infrastructure.Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(Infrastructure.Constants.Provider) as FirebirdAdapterFactory
            ?? throw new InvalidOperationException("The Firebird adapter is absent from the data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);
        await using var connection = new FbConnection(route.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM RDB$DATABASE";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
