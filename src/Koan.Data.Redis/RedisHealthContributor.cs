using Koan.Core;
using Koan.Core.Observability.Health;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Koan.Data.Redis;

internal sealed class RedisHealthContributor(IServiceProvider serviceProvider) : IHealthContributor
{
    public string Name => "data:redis";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Lazy resolve the connection multiplexer only when health check runs
            var muxer = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var db = muxer.GetDatabase();
            // PING check
            var pong = await db.PingAsync();
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, new Dictionary<string, object?> { ["latencyMs"] = pong.TotalMilliseconds });
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}
