using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Connector.ZenGarden.Initialization;

public sealed class ZenGardenAiModule : KoanModule, IContributeTo<AiProviderContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddSingleton<ZenGardenAiRuntime>();
        services.TryAddSingleton(sp => new ZenGardenAiAdapter(
            sp.GetRequiredService<ZenGardenAiRuntime>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ZenGardenAiAdapter>>()));
    }

    public void Contribute(AiProviderContributionTarget target) =>
        target.Add<ZenGardenAiAdapterContributor>(Infrastructure.Constants.Adapter.Id);

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Layered Zen Garden AI orchestration: available when a ready orchestrator offering resolves.");
    }
}
