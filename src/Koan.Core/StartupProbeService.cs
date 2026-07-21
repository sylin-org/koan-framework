using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Observability.Health;
using Koan.Core.Observability.Probes;

namespace Koan.Core;

internal sealed class StartupProbeService : BackgroundService
{
    private readonly Koan.Core.Observability.Health.IHealthAggregator _agg;
    private readonly IHealthRegistry _registry;
    private readonly ILogger<StartupProbeService>? _log;

    public StartupProbeService(Koan.Core.Observability.Health.IHealthAggregator agg, IHealthRegistry registry, ILogger<StartupProbeService>? log = null)
    { _agg = agg; _registry = registry; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _log?.LogDebug("StartupProbe: running {Count} contributors to seed snapshot", _registry.All.Count);
            foreach (var c in _registry.All)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    var report = await c.Check(stoppingToken).ConfigureAwait(false);
                    stoppingToken.ThrowIfCancellationRequested();
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
                            dict[kv.Key] = kv.Value?.ToString() ?? "";
                        }
                        facts = dict;
                    }
                    var enriched = facts is null ? new Dictionary<string, string>() : new Dictionary<string, string>(facts);
                    enriched["critical"] = c.IsCritical ? "true" : "false";
                    _agg.Push(c.Name, status, report.Description, ttl: null, facts: enriched);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    _log?.LogDebug("StartupProbe: {Name} threw, marking unhealthy", c.Name);
                    _agg.Push(c.Name, HealthStatus.Unhealthy, "exception during health check");
                }
            }

            stoppingToken.ThrowIfCancellationRequested();

            // Also invite listeners. RequestProbe dispatch is synchronous, so this remains part of
            // the tracked BackgroundService task and receives the same host-owned stopping token.
            _log?.LogDebug("StartupProbe: broadcasting initial probe");
            _agg.RequestProbe(ProbeReason.Startup, component: null, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "StartupProbe: unexpected failure while seeding health snapshot");
        }
    }
}
