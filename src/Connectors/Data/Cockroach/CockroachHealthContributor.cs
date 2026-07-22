using Npgsql;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Cockroach;

/// <summary>Reports readiness only for CockroachDB sources that participate in this application.</summary>
internal sealed class CockroachHealthContributor : DataAdapterHealthContributorBase
{
    private const string ProviderName = Infrastructure.Constants.Provider.Name;
    private readonly IServiceProvider _services;
    private readonly CockroachAdapterFactory _factory;

    public CockroachHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(ProviderName, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(ProviderName) as CockroachAdapterFactory
            ?? throw new InvalidOperationException("The CockroachDB provider is absent from the host Data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var options = _factory.ResolveOptions(_services, source);
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
