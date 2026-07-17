using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Compute.Initialization;

/// <summary>
/// Auto-registers the compute fabric service when Koan.AI.Compute is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class AiComputeModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<IComputeService, ComputeService>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Compute fabric registered (local detection active).");
    }
}
