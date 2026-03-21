using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Models.Initialization;

/// <summary>
/// Auto-registers the model catalog service when Koan.AI.Models is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Models";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IModelService, ModelService>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Model catalog registered (adapter-first resolution via IAiAdapterRegistry, Entity<ModelEntry> catalog).");
    }
}
