using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.DuckDb.Infrastructure;
using Koan.Data.Connector.DuckDb.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.DuckDb;

internal sealed class DuckDbHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly DuckDbAdapterFactory _factory;
    private readonly DuckDbConnections _connections;

    public DuckDbHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider,
        DuckDbConnections connections)
        : base(Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _connections = connections;
        _factory = providers.Find(Constants.Provider) as DuckDbAdapterFactory
            ?? throw new InvalidOperationException("The DuckDB provider is absent from the host Data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);

        // A managed file database that has not been created yet is not a fault. The probe deliberately opens
        // non-creating, so on a fresh host it would otherwise report "unavailable" for a store that is simply
        // waiting to be provisioned — a 503 on the first boot of an application that is working perfectly.
        // An external store is different: its absence is a real dependency failure and still fails.
        if (route.Policy.StorageLifecycle == StorageLifecycle.Managed && IsAwaitingProvisioning(route.ConnectionString))
        {
            return;
        }

        await using var connection = _connections.Create(route.ConnectionString, route.Source, nonCreating: true);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this managed route is simply not provisioned yet, as opposed to broken.
    ///
    /// <para>The distinction is the parent directory. A missing database file under a usable directory is
    /// what every fresh host looks like before its first write. A missing file whose parent is absent — or
    /// is itself a file — is a real fault and still probes, and still fails.</para>
    /// </summary>
    private bool IsAwaitingProvisioning(string connectionString)
    {
        try
        {
            var (path, isMemory) = _connections.DescribeSource(connectionString);
            if (isMemory || string.IsNullOrWhiteSpace(path)) return false;
            if (path.Contains("://", StringComparison.Ordinal)) return false;

            var anchored = _connections.AnchorDataSource(path);
            if (File.Exists(anchored)) return false;

            var directory = Path.GetDirectoryName(anchored);
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }
}
