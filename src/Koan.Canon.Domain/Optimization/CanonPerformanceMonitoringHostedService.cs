using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Koan.Canon.Domain.Optimization;

internal sealed class CanonPerformanceMonitoringHostedService : IHostedService, IAsyncDisposable
{
    private readonly CanonPerformanceMonitor _monitor;
    private readonly CanonOptimizationOptions _options;
    private CancellationTokenSource? _cts;

    public CanonPerformanceMonitoringHostedService(CanonPerformanceMonitor monitor, CanonOptimizationOptions options)
    {
        _monitor = monitor;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Monitoring.Enabled)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitor.Start(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        await _monitor.DisposeAsync();
    }

    public ValueTask DisposeAsync()
        => _monitor.DisposeAsync();
}
