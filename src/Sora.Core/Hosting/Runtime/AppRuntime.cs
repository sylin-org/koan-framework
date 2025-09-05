using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Sora.Core.Hosting.Bootstrap;
using Sora.Core.Observability;
using Sora.Core;

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
            
            // Collect module information from all SoraAutoRegistrars
            CollectBootReport(report, cfg);
            
            var show = !SoraEnv.IsProduction;
            var obs = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.Core.Observability.ObservabilityOptions>>();
            if (!show)
                show = obs?.Value?.Enabled == true && obs.Value?.Traces?.Enabled == true;
                
            if (show && cfg != null)
            {
                var options = GetBootReportOptions(cfg);
                try { Console.Write(report.ToString(options)); } catch { }
            }
        }
        catch { /* best-effort */ }
    }

    private void CollectBootReport(BootReport report, IConfiguration? cfg)
    {
        if (cfg == null) return;
        
        var env = _sp.GetService<IHostEnvironment>();
        
        // Find and invoke all SoraAutoRegistrars to collect their reports
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            
            foreach (var t in types)
            {
                if (t.IsAbstract || !typeof(ISoraAutoRegistrar).IsAssignableFrom(t)) continue;
                try
                {
                    if (Activator.CreateInstance(t) is ISoraAutoRegistrar registrar)
                    {
                        var hostEnv = env ?? new DefaultHostEnvironment();
                        registrar.Describe(report, cfg, hostEnv);
                    }
                }
                catch { /* best-effort */ }
            }
        }
    }

    private static BootReportOptions GetBootReportOptions(IConfiguration cfg)
    {
        var section = cfg.GetSection("Sora:Bootstrap");
        return new BootReportOptions
        {
            ShowDecisions = section.GetValue("ShowDecisions", !SoraEnv.IsProduction),
            ShowConnectionAttempts = section.GetValue("ShowConnectionAttempts", !SoraEnv.IsProduction), 
            ShowDiscovery = section.GetValue("ShowDiscovery", !SoraEnv.IsProduction),
            CompactMode = section.GetValue("CompactMode", SoraEnv.IsProduction)
        };
    }

    public void Start()
    {
        // No-op by default; features (e.g., data) can hook into hosted services for start-up work.
        // Keep best-effort guards and avoid throwing during host start.
        try { /* intentional no-op */ } catch { }
    }
}

/// <summary>
/// Simple default implementation of IHostEnvironment for fallback scenarios
/// </summary>
internal class DefaultHostEnvironment : IHostEnvironment
{
    public string ApplicationName { get; set; } = "SoraApp";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Production;
}
