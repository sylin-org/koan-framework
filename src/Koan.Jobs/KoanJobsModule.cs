using Koan.Core;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core.Semantics;
using Koan.Core.Composition;
using Koan.Jobs.Composition;

namespace Koan.Jobs;

/// <summary>
/// Boot module for the Jobs pillar (ARCH-0086). Auto-discovered via <see cref="KoanModule"/>; registers the
/// orchestrator + ledger + coordinator + worker, and self-reports the elected tier in the boot report.
/// </summary>
public sealed class KoanJobsModule : KoanModule
{
    public override void Register(IServiceCollection services) => services.AddKoanJobs();

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("jobs.types", b => b
            .Label("Discovered job types")
            .Value(JobTypeRegistry.FromDiscovery().Count.ToString()));
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => JobsCompositionFacts.Project(composition, services, GetType().FullName ?? Id);
}
