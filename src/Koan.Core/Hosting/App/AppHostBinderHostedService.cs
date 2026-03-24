using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core.Hosting.App;

namespace Koan.Core.Hosting.App;

// Generic-host binder: ensures AppHost.Current is set and KoanEnv is initialized early
internal sealed class AppHostBinderHostedService(System.IServiceProvider sp) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Set ambient host once
        if (AppHost.Current is null)
            AppHost.Current = sp;
        try { KoanEnv.TryInitialize(sp); } catch { }

        try
        {
            var cfg = sp.GetService(typeof(IConfiguration)) as IConfiguration;
            var env = sp.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
            AppHost.SetIdentity(global::Koan.Core.Hosting.App.ApplicationIdentityDefaults.Resolve(cfg, env));
        }
        catch
        {
            // identity population is best-effort; never block host startup
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
