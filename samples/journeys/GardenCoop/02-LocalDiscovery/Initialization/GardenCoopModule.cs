using GardenCoop.Automation;
using GardenCoop.Infrastructure;
using Koan.AI.Initialization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GardenCoop.Initialization;

/// <summary>Owns GardenCoop's business-rule composition, starter data, and startup explanation.</summary>
[After(typeof(AiModule))]
public sealed class GardenCoopModule : KoanModule
{
    public override void Register(IServiceCollection services) => GardenAutomation.Configure();

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<GardenCoopModule>();
        await GardenSeeder.EnsureSampleData(logger, ct);
        await ProduceCatalog.EnsureStarterListings(logger, ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Garden automation plus local semantic discovery for the cooperative harvest.");
        module.SetSetting("automation", setting => setting
            .Label("Garden automation")
            .Description("Recent soil readings create or acknowledge one watering reminder per plot.")
            .Value($"window={GardenAutomation.ReadingWindowSize}; dryBelow={GardenAutomation.DrySoilThreshold:F1}%")
            .Source(ProvenanceSettingSource.Custom)
            .State(ProvenanceSettingState.Configured));
        module.AddSetting("local-discovery", "ripe red tomato → Heirloom Tomatoes");
        module.AddNote("Saving Produce creates its embedding and local vector index automatically.");
    }
}
