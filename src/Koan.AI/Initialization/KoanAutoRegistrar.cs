using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.AI.Pillars;

namespace Koan.AI.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        AiPillarManifest.EnsureRegistered();
        // Bind options if IConfiguration is present later; AddAi also binds when config is provided.
        services.AddAi();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        // Minimal for now; providers will append their own notes/settings.
        report.AddNote("AI core registered (facade, registry, router). Providers pending.");
    }
}
