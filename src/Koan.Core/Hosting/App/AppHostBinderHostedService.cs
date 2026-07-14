using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Koan.Core.Hosting.App;

// Generic-host binder: ensures AppHost.Current is set and KoanEnv is initialized early
internal sealed class AppHostBinderHostedService(System.IServiceProvider sp) : IHostedService
{
    private IDisposable? _hostLease;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _hostLease, null)?.Dispose();
        _hostLease = AppHost.Attach(sp);
        try { KoanEnv.TryInitialize(sp); } catch { }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _hostLease, null)?.Dispose();
        return Task.CompletedTask;
    }
}
