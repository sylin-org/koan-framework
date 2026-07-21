using Koan.AI.Web.Controllers;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Web.Initialization;

/// <summary>
/// Makes the provider-neutral AI HTTP projection available when the package is referenced.
/// </summary>
public sealed class AiWebModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanControllersFrom<AiController>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddTool(
            "AI HTTP API",
            $"/{Constants.Routes.Base}",
            "Provider-neutral chat, embeddings, model inventory, and capability inspection",
            capability: "ai.http");
    }
}
