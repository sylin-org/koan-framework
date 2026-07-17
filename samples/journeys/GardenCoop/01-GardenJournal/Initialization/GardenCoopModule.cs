using GardenCoop.Automation;
using GardenCoop.Infrastructure;
using Koan.Core;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GardenCoop.Initialization;

/// <summary>Owns GardenCoop's business-rule composition, starter data, and startup explanation.</summary>
public sealed class GardenCoopModule : KoanModule
{
    public override void Register(IServiceCollection services) => GardenAutomation.Configure();

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<GardenCoopModule>();
        return GardenSeeder.EnsureSampleData(logger, ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Garden sensor binding and watering-reminder automation.");
        module.SetSetting("automation", setting => setting
            .Label("Garden automation")
            .Description("Recent soil readings create or acknowledge one watering reminder per plot.")
            .Value($"window={GardenAutomation.ReadingWindowSize}; dryBelow={GardenAutomation.DrySoilThreshold:F1}%")
            .Source(ProvenanceSettingSource.Custom)
            .State(ProvenanceSettingState.Configured));
    }
}
