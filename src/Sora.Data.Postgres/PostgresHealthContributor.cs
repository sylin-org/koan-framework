using Microsoft.Extensions.Options;
using Npgsql;
using Sora.Core;

namespace Sora.Data.Postgres;

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
            return new HealthReport(Name, HealthState.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}