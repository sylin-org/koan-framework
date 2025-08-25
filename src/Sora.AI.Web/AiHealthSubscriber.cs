using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Routing;
using Sora.Core;

namespace Sora.AI.Web;

/// Subscribes to the Health Aggregator to report AI service health.
internal sealed class AiHealthSubscriber : IHostedService
{
    private readonly IHealthAggregator _agg;
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<AiHealthSubscriber> _log;
    private IDisposable? _sub;

    public AiHealthSubscriber(IHealthAggregator agg, IAiAdapterRegistry registry, ILogger<AiHealthSubscriber> log)
    { _agg = agg; _registry = registry; _log = log; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Targeted subscription for component "ai"
    _sub = _agg.Subscribe("ai", e => { _ = PushAsync(); });
        // Optionally also respond to broadcasts by pushing
        _handler ??= OnProbeRequested;
        _agg.ProbeRequested += _handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sub?.Dispose();
        _sub = null;
        if (_handler is not null) _agg.ProbeRequested -= _handler;
        return Task.CompletedTask;
    }

    private Task PushAsync()
    {
        try
        {
            var all = _registry.All.ToList();
            var ready = all.Count; // Using total count as readiness indicator; detailed reachability is checked by contributors
            _agg.Push(
                component: "ai",
                status: HealthStatus.Healthy,
                message: "ok",
                ttl: TimeSpan.FromSeconds(30),
                facts: new Dictionary<string, string>
                {
                    ["adapters"] = all.Count.ToString(),
                    ["adaptersReady"] = ready.ToString(),
                }
            );
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AI health push failed");
            _agg.Push("ai", HealthStatus.Degraded, ex.Message, ttl: TimeSpan.FromSeconds(15));
        }
        return Task.CompletedTask;
    }

    private EventHandler<ProbeRequestedEventArgs>? _handler;
    private void OnProbeRequested(object? sender, ProbeRequestedEventArgs e)
    {
        if (e.Component is null) _ = PushAsync();
    }
}
