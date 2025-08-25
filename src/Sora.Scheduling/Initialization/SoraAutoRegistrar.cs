using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;

namespace Sora.Scheduling.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Scheduling";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Options: Sora:Scheduling
        services.AddSoraOptions<SchedulingOptions>(cfg, "Sora:Scheduling")
            .PostConfigure(opts =>
            {
                // Dev default enabled, Prod default disabled unless explicitly enabled
                if (!env.IsDevelopment() && !cfg.GetSection("Sora:Scheduling").Exists())
                {
                    opts.Enabled = false;
                }
            });

        // Tasks are expected to self-register via Sora.Core ISoraInitializer in their own assemblies.
        services.AddHostedService<SchedulingOrchestrator>();
    }

    // Required by ISoraInitializer; minimal registration without bespoke discovery.
    public void Initialize(IServiceCollection services)
    {
        services.AddSoraOptions<SchedulingOptions>("Sora:Scheduling");
        services.AddHostedService<SchedulingOrchestrator>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var enabled = cfg["Sora:Scheduling:Enabled"]; // may be null
        report.AddSetting("enabled", enabled ?? (env.IsDevelopment() ? "(default true)" : "(default false)"));
        report.AddSetting("readinessGate", cfg["Sora:Scheduling:ReadinessGate"] ?? "true");
        // Discovery count omitted; tasks self-register using Sora.Core initialization.
    }
}
