using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Observability;
using Sora.Core.Hosting.Bootstrap;

namespace Sora.Core.Hosting.Runtime;

internal sealed class AppRuntime : IAppRuntime
{
    private readonly IServiceProvider _sp;
    public AppRuntime(IServiceProvider sp) { _sp = sp; }

    public void Discover()
    {
        // Initialize SoraEnv and print a bootstrap report if enabled
        try { SoraEnv.TryInitialize(_sp); } catch { }
        try
        {
            var report = new BootReport();
            var cfg = _sp.GetService<IConfiguration>();
            var show = !SoraEnv.IsProduction;
            var obs = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.Core.Observability.ObservabilityOptions>>();
            if (!show)
                show = obs?.Value?.Enabled == true && obs.Value?.Traces?.Enabled == true;
            if (show)
            {
                try { Console.Write(report.ToString()); } catch { }
            }
        }
        catch { /* best-effort */ }
    }

    public void Start()
    {
        // No-op by default; features (e.g., data) can hook into hosted services for start-up work.
        // Keep best-effort guards and avoid throwing during host start.
        try { /* intentional no-op */ } catch { }
    }
}
