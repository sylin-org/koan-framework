using System.Collections.Concurrent;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly SqliteAdapterFactory _factory;
    private readonly SqliteConnections _connections;
    private readonly IOptions<SqliteOptions> _options;
    // The journal mode each probed source's database reported, so readiness shows the mode in effect, not the one asked for.
    private readonly ConcurrentDictionary<string, string> _journalModes = new(StringComparer.OrdinalIgnoreCase);

    public SqliteHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider,
        SqliteConnections connections,
        IOptions<SqliteOptions> options)
        : base(Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _connections = connections;
        _options = options;
        _factory = providers.Find(Constants.Provider) as SqliteAdapterFactory
            ?? throw new InvalidOperationException("The SQLite provider is absent from the host Data catalog.");
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
            _journalModes.TryRemove(source, out _);
            return;
        }

        await using var connection = await _connections.Open(route.ConnectionString, route.Source, ct, nonCreating: true)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        // A read-only connection cannot change the journal mode, so what it reads is the mode the database holds.
        var journalMode = await SqliteConnections.ReadJournalMode(connection, ct).ConfigureAwait(false);
        if (journalMode is null) _journalModes.TryRemove(source, out _);
        else _journalModes[source] = journalMode;
    }

    /// <summary>The configured posture, and per probed source the journal mode its database reported.</summary>
    protected override IReadOnlyDictionary<string, object?> HealthyData(IReadOnlyCollection<string> sources)
    {
        var data = new Dictionary<string, object?>(base.HealthyData(sources), StringComparer.Ordinal);
        var posture = _options.Value;
        data["journalMode"] = SqliteConnections.Spell(posture.JournalMode);
        data["busyTimeout"] = posture.BusyTimeout.ToString();
        data["synchronous"] = SqliteConnections.Spell(posture.Synchronous);
        data["journalModes"] = string.Join(",", sources
            .Where(_journalModes.ContainsKey)
            .Select(source => $"{source}={_journalModes[source]}"));
        return data;
    }

    /// <summary>
    /// Whether this managed route is simply not provisioned yet, as opposed to broken.
    ///
    /// <para>The distinction is the nearest existing ancestor of the database file. Provisioning creates every
    /// missing directory, so a missing file under a missing <c>.koan/data</c> beneath a usable directory is what
    /// every fresh zero-config host looks like before its first write (NK-008). An ancestor that is a file is a real
    /// fault and still probes, and still fails.</para>
    /// </summary>
    private bool IsAwaitingProvisioning(string connectionString)
    {
        try
        {
            var builder = _connections.Parse(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource)) return false;
            if (builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)) return false;
            if (builder.DataSource.Contains("://", StringComparison.Ordinal)) return false;

            var path = _connections.AnchorDataSource(builder.DataSource);
            if (File.Exists(path)) return false;

            for (var directory = Path.GetDirectoryName(path);
                 !string.IsNullOrWhiteSpace(directory);
                 directory = Path.GetDirectoryName(directory))
            {
                if (Directory.Exists(directory)) return true;
                if (File.Exists(directory)) return false;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
