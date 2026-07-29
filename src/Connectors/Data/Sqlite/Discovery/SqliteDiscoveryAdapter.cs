using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Sqlite.Discovery;

internal sealed class SqliteDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<SqliteDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Provider;
    public override string[] Aliases => ["sqlite3"];

    protected override Type GetFactoryType() => typeof(SqliteAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.Keys.DefaultSourceConnectionString] ??
        _configuration[Constants.Configuration.Keys.ProviderSourceConnectionString] ??
        _configuration[Constants.Configuration.Keys.ConnectionString] ??
        _configuration.GetConnectionString("Sqlite");

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates() => [];

    protected override string? ReadAspireServiceDiscovery() => null;

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(serviceUrl);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception error)
        {
            logger.LogWarning("sqlite.health probe-failed connection={Connection} error={Error}",
                Redaction.DeIdentify(serviceUrl), Redaction.DeIdentify(error.Message));
            return false;
        }
    }
}
