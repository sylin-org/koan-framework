using Koan.Core.Observability.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Core.Adapters;

internal sealed class AdapterReadinessMonitor : IHostedService
{
    private readonly IEnumerable<IAdapterReadiness> _adapters;
    private readonly IHealthAggregator _aggregator;
    private readonly ILogger<AdapterReadinessMonitor> _logger;
    private readonly List<(IAdapterReadiness Adapter, EventHandler<ReadinessStateChangedEventArgs> Handler)> _subscriptions = new();
    private readonly AdaptersReadinessOptions _options;

    public AdapterReadinessMonitor(
        IEnumerable<IAdapterReadiness> adapters,
        IHealthAggregator aggregator,
        ILogger<AdapterReadinessMonitor> logger,
        IOptions<AdaptersReadinessOptions> options)
    {
        _adapters = adapters;
        _aggregator = aggregator;
        _logger = logger;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableMonitoring)
        {
            _logger.LogDebug("Adapter readiness monitoring disabled via configuration");
            return Task.CompletedTask;
        }

        foreach (var adapter in _adapters.Distinct())
        {
            void Handler(object? sender, ReadinessStateChangedEventArgs args) => Publish(adapter, args.CurrentState, args.TimestampUtc);
            adapter.StateManager.StateChanged += Handler;
            _subscriptions.Add((adapter, Handler));
            Publish(adapter, adapter.ReadinessState, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var (adapter, handler) in _subscriptions)
        {
            adapter.StateManager.StateChanged -= handler;
        }

        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    private void Publish(IAdapterReadiness readiness, AdapterReadinessState state, DateTime timestamp)
    {
        var component = $"adapter:{readiness.GetType().Name}";
        var status = state switch
        {
            AdapterReadinessState.Ready => HealthStatus.Healthy,
            AdapterReadinessState.Degraded => HealthStatus.Degraded,
            AdapterReadinessState.Failed => HealthStatus.Unhealthy,
            _ => HealthStatus.Unknown
        };

        var facts = new Dictionary<string, string>
        {
            ["state"] = state.ToString(),
            ["is_ready"] = readiness.IsReady.ToString().ToLowerInvariant(),
            ["timestamp"] = timestamp.ToString("O")
        };

        _logger.LogInformation("Adapter {Adapter} transitioned to {State}", readiness.GetType().Name, state);
        _aggregator.Push(component, status, message: null, facts: facts);
    }
}
