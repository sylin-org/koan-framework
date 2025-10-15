using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Scheduling.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Scheduling";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Options: Koan:Scheduling
        services.AddKoanOptions<SchedulingOptions>(cfg, "Koan:Scheduling")
            .PostConfigure(opts =>
            {
                // Dev default enabled, Prod default disabled unless explicitly enabled
                if (!env.IsDevelopment() && !cfg.GetSection("Koan:Scheduling").Exists())
                {
                    opts.Enabled = false;
                }
            });

        // Tasks are expected to self-register via Koan.Core IKoanInitializer in their own assemblies.
        services.AddHostedService<SchedulingOrchestrator>();
    }

    // Required by IKoanInitializer; minimal registration without bespoke discovery.
    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<SchedulingOptions>("Koan:Scheduling");
        services.AddHostedService<SchedulingOrchestrator>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var enabled = cfg["Koan:Scheduling:Enabled"]; // may be null
        module.AddSetting("enabled", enabled ?? (env.IsDevelopment() ? "(default true)" : "(default false)"));
        module.AddSetting("readinessGate", cfg["Koan:Scheduling:ReadinessGate"] ?? "true");
        // Discovery count omitted; tasks self-register using Koan.Core initialization.
    }
}

