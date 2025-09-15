using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Koan.Core.Hosting.App;

// Generic-host binder: ensures AppHost.Current is set and KoanEnv is initialized early
internal sealed class AppHostBinderHostedService : IHostedService
{
    private readonly System.IServiceProvider _sp;

    public AppHostBinderHostedService(System.IServiceProvider sp)
    {
        _sp = sp;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Set ambient host once
        if (AppHost.Current is null)
            AppHost.Current = _sp;
        try { KoanEnv.TryInitialize(_sp); } catch { }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
