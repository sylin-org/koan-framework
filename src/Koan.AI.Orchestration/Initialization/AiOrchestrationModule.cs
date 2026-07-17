using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Orchestration.Initialization;

/// <summary>
/// Auto-registers the chain executor when Koan.AI.Orchestration is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class AiOrchestrationModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<IChainExecutor, ChainExecutor>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Chain composition engine registered.");
    }
}
