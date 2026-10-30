using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Koan.Core.Context;

namespace Koan.Core.Hosting.App;

/// <summary>
/// Generic-host binder: attaches <see cref="AppHost.Current"/> and initializes <c>KoanEnv</c> in
/// <see cref="StartingAsync"/>, which the host runs before any hosted service's <c>StartAsync</c>, so an application
/// hosted service registered before <c>AddKoan()</c> still starts with the host attached (ARCH-0140). The host is
/// released in <see cref="StoppedAsync"/>, after every hosted service has stopped.
/// </summary>
internal sealed class AppHostBinderHostedService(
    System.IServiceProvider sp,
    KoanContextCarrierRegistry? contextCarriers = null) : IHostedLifecycleService
{
    private IDisposable? _hostLease;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Force validation of this host's carrier composition before application work starts. The registry itself is
        // host-owned; resolving it here does not attach logical-flow context to the host.
        _ = contextCarriers;
        Interlocked.Exchange(ref _hostLease, null)?.Dispose();
        _hostLease = AppHost.Attach(sp);
        try { KoanEnv.TryInitialize(sp); } catch { }
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _hostLease, null)?.Dispose();
        return Task.CompletedTask;
    }
}
