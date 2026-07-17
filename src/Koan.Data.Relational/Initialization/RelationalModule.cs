using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Relational.Initialization;

public sealed class RelationalModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Toolkit only; providers like Sqlite/Mongo use it. No DI wiring by default.
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Relational toolkit loaded (LINQ translator + schema model)");
    }
}
