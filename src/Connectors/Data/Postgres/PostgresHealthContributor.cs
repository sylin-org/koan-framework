using Npgsql;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Postgres;

internal sealed class PostgresHealthContributor : DataAdapterHealthContributorBase
{
    private const string ProviderName = "postgres";
    private readonly IServiceProvider _services;
    private readonly PostgresAdapterFactory _factory;

    public PostgresHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(ProviderName, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(ProviderName) as PostgresAdapterFactory
            ?? throw new InvalidOperationException("The PostgreSQL provider is absent from the host Data catalog.");
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
