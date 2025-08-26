using Microsoft.Extensions.Logging;
using Sora.Core.Observability.Health;
using Sora.Core.Observability.Probes;

namespace Sora.Core;

internal sealed class StartupProbeService : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly Sora.Core.Observability.Health.IHealthAggregator _agg;
    private readonly IHealthRegistry _registry;
    private readonly ILogger<StartupProbeService>? _log;
    public StartupProbeService(Sora.Core.Observability.Health.IHealthAggregator agg, IHealthRegistry registry, ILogger<StartupProbeService>? log = null)
    { _agg = agg; _registry = registry; _log = log; }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Run initial probe in the background so host startup is not blocked
        _ = Task.Run(async () =>
        {
            try
            {
                _log?.LogDebug("StartupProbe: running {Count} contributors to seed snapshot", _registry.All.Count);
                foreach (var c in _registry.All)
                {
                    try
                    {
                        var report = await c.CheckAsync(cancellationToken).ConfigureAwait(false);
                        _log?.LogDebug("StartupProbe: {Name} -> {State} ({Msg})", c.Name, report.State, report.Description);
                        var status = report.State switch
                        {
                            HealthState.Healthy => HealthStatus.Healthy,
                            HealthState.Degraded => HealthStatus.Degraded,
                            HealthState.Unhealthy => HealthStatus.Unhealthy,
                            _ => HealthStatus.Unknown
                        };
                        IReadOnlyDictionary<string, string>? facts = null;
                        if (report.Data is not null && report.Data.Count > 0)
                        {
                            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var kv in report.Data)
                            {
                                if (kv.Value is null) continue;
                                dict[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                            }
                            facts = dict;
                        }
                        var enriched = facts is null ? new Dictionary<string, string>() : new Dictionary<string, string>(facts);
                        enriched["critical"] = c.IsCritical ? "true" : "false";
                        _agg.Push(c.Name, status, report.Description, ttl: null, facts: enriched);
                    }
                    catch
                    {
                        _log?.LogDebug("StartupProbe: {Name} threw, marking unhealthy", c.Name);
                        _agg.Push(c.Name, HealthStatus.Unhealthy, "exception during health check");
                    }
                }
                // Also invite async listeners
                _log?.LogDebug("StartupProbe: broadcasting initial probe");
                _agg.RequestProbe(ProbeReason.Startup, component: null, cancellationToken);
            }
            catch { }
        }, cancellationToken);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}