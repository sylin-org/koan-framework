using Microsoft.Extensions.Options;
using Npgsql;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Postgres;

internal sealed class PostgresHealthContributor(IOptions<PostgresOptions> options) : IHealthContributor
{
    public string Name => "data:postgres";
    public bool IsCritical => true;
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            _ = await cmd.ExecuteScalarAsync(ct);
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, null);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}