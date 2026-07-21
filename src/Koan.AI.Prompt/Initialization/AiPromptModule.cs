using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Prompt.Initialization;

/// <summary>
/// Projects the availability of the optional Entity-backed prompt catalog.
/// </summary>
public sealed class AiPromptModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Entity-backed prompt catalog available; Prompt values remain part of AI contracts.");
    }
}
