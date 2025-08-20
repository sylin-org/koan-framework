using Sora.Core;
using StackExchange.Redis;

namespace Sora.Data.Redis;

internal sealed class RedisHealthContributor(IConnectionMultiplexer muxer) : IHealthContributor
{
    public string Name => "data:redis";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var db = muxer.GetDatabase();
            // PING check
            var pong = await db.PingAsync();
            return new HealthReport(Name, HealthState.Healthy, null, null, new Dictionary<string, object?> { ["latencyMs"] = pong.TotalMilliseconds });
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex, null);
        }
    }
}
