using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Cutover.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Cutover.Initialization;

public sealed class DataCutoverModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<DataCutoverOptions>(Infrastructure.Constants.ConfigurationSection);
        services.TryAddSingleton<Runtime.DefaultRouteTransitionService>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Enables verified, provider-bounded promotion of a configured Data source to the active default.");
    }
}
