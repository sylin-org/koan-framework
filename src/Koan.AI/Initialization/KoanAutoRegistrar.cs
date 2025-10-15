using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.AI.Pillars;
using Koan.Core.Hosting.Bootstrap;

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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Minimal for now; providers will append their own notes/settings.
        module.AddNote("AI core registered (facade, registry, router). Providers pending.");
    }
}

