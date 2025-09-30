using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteHealthContributor(IOptions<SqliteOptions> options) : IHealthContributor
{
    public string Name => "data:sqlite";
    public bool IsCritical => true;
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            // trivial probe
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            _ = await cmd.ExecuteScalarAsync(ct);
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, null);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}
