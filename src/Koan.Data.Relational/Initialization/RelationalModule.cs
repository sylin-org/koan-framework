using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Data.Relational.Initialization;

public sealed class RelationalModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddSingleton<IRelationalSchemaOrchestrator, RelationalSchemaOrchestrator>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Relational schema policy and translation mechanics are compiled once; providers supply route-specific decisions.");
    }
}
