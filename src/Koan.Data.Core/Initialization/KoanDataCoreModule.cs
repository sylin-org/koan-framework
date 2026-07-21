using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Data.Core.Pillars;
using Koan.Data.Core.Composition;
using Koan.Core.Composition;
using Koan.Core.Semantics;
using DataCoreItems = Koan.Data.Core.Infrastructure.DataCoreProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Core.Initialization;

/// <summary>
/// Koan.Data.Core boot module (ARCH-0086). Folds the former split between <c>KoanDataCoreInitializer</c>
/// service registration, pillar manifest, and provenance into one
/// <see cref="KoanModule"/>: <see cref="Register"/> wires the data-core services and ensures the data
/// pillar manifest; <see cref="Report"/> publishes the EnsureSchemaOnStart provenance. Id preserves the
/// prior ModuleName so boot reports are unchanged.
/// </summary>
public sealed class KoanDataCoreModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        DataPillarManifest.EnsureRegistered();
        ServiceCollectionExtensions.RegisterKoanDataCoreServices(services);
    }

    public override System.Threading.Tasks.Task Start(IServiceProvider services, System.Threading.CancellationToken ct)
    {
        // ARCH-0101 §8: the boot-refuses-leaky-axis pre-flight. Loud warning in Development (boot continues), boot
        // refusal in Production. No-op when no always-on predicate axis is registered (off = byte-identical).
        var env = services.GetRequiredService<IHostEnvironment>();
        var logger = services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Axes");
        Axes.DataAxisPreflight.Run(services, env, logger);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var ensureSetting = Koan.Core.Configuration.ReadWithSource(
            cfg,
            DataCoreItems.EnsureSchemaOnStart.Key,
            true);

        module.AddSetting(
            DataCoreItems.EnsureSchemaOnStart,
            ProvenanceModes.FromConfigurationValue(ensureSetting),
            ensureSetting.Value,
            sourceKey: ensureSetting.ResolvedKey,
            usedDefault: ensureSetting.UsedDefault);

        // ARCH-0101 §9: list the active data-axis planes (managed isolation fields, operation overrides, container
        // particles) in the boot report. Omitted entirely when no axis is registered (off = structurally absent).
        if (Axes.DataAxisReport.Summarize() is { } axes)
            module.SetSetting("DataAxes", b => b.Value(axes));
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => DataCompositionFacts.Project(composition, services, GetType().FullName ?? Id);
}
