using Microsoft.Extensions.Hosting;
using Sora.Core.Observability.Probes;
using Sora.Core.Observability.Health;

namespace Sora.Core;

/// Bridges legacy IHealthContributor checks into the push-first IHealthAggregator.
/// Subscribes targeted handlers per contributor name and also listens to broadcast probes.
internal sealed class HealthContributorsBridge : IHostedService
{
    private readonly IHealthRegistry _registry;
    private readonly Sora.Core.Observability.Health.IHealthAggregator _agg;
    private readonly List<IDisposable> _subscriptions = new();

    public HealthContributorsBridge(IHealthRegistry registry, Sora.Core.Observability.Health.IHealthAggregator agg)
    { _registry = registry; _agg = agg; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Targeted subscriptions per contributor name
        foreach (var c in _registry.All)
        {
            // Run checks synchronously for predictable readiness updates
            var unsub = _agg.Subscribe(c.Name, args => { RunContributorSync(c, cancellationToken); });
            _subscriptions.Add(unsub);
        }
        // Fallback: listen to broadcast events to run all contributors when no specific targeting is used
        _agg.ProbeRequested += OnProbeRequested;
        return Task.CompletedTask;

    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _agg.ProbeRequested -= OnProbeRequested;
        // Dispose subscriptions using a snapshot to avoid modifying the collection while iterating
        var snapshot = _subscriptions.ToArray();
        foreach (var d in snapshot) { try { d.Dispose(); } catch { /* best-effort */ } }
        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    private void OnProbeRequested(object? sender, ProbeRequestedEventArgs e)
    {
        if (e.Component is not null) return; // targeted handled via scoped subscription
        // Broadcast: run all contributors quickly in background
        foreach (var c in _registry.All)
        {
            // Synchronous to ensure snapshot reflects latest results when requested
            RunContributorSync(c, CancellationToken.None);
        }
    }

    private async Task RunContributorAsync(IHealthContributor c, CancellationToken ct)
    {
        try
        {
            var report = await c.CheckAsync(ct).ConfigureAwait(false);
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
            _agg.Push(c.Name, HealthStatus.Unhealthy, "exception during health check");
        }
    }

    private void RunContributorSync(IHealthContributor c, CancellationToken ct)
    {
        try
        {
            var report = c.CheckAsync(ct).GetAwaiter().GetResult();
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
            _agg.Push(c.Name, HealthStatus.Unhealthy, "exception during health check");
        }
    }
}
