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
public sealed class AiModelsModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<IModelService, ModelService>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Model catalog registered (capability-based resolution via IAiAdapterRegistry + AdapterResolver).");
    }
}
