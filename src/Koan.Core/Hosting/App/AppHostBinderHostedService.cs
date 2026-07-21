using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Koan.Core.Context;

namespace Koan.Core.Hosting.App;

// Generic-host binder: ensures AppHost.Current is set and KoanEnv is initialized early
internal sealed class AppHostBinderHostedService(
    System.IServiceProvider sp,
    KoanContextCarrierRegistry? contextCarriers = null) : IHostedService
{
    private IDisposable? _hostLease;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Force validation of this host's carrier composition before application work starts. The registry itself is
        // host-owned; resolving it here does not attach logical-flow context to the host.
        _ = contextCarriers;
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
