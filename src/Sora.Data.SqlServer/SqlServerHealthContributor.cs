using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Sora.Core;

namespace Sora.Data.SqlServer;

internal sealed class SqlServerHealthContributor(IOptions<SqlServerOptions> options) : IHealthContributor
{
    public string Name => "data:sqlserver";
    public bool IsCritical => true;
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            _ = await cmd.ExecuteScalarAsync(ct);
            return new HealthReport(Name, HealthState.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}