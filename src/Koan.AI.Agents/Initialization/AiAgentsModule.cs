using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Agents.Initialization;

/// <summary>
/// Auto-registers the agent executor when Koan.AI.Agents is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class AiAgentsModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<IAgentExecutor, AgentExecutor>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Entity-aware agent engine registered.");
    }
}
