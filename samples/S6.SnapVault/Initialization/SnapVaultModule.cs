using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.AI;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.ZenGarden;
using S6.SnapVault.Configuration;

namespace S6.SnapVault.Initialization;

/// <summary>
/// SnapVault's single self-describing boot unit (ARCH-0086). Owns config binding, boot-time seeding of the
/// system analysis-style library, the ZenGarden model-advisor log line, and the boot self-report. Domain
/// services (photo processing, set queries, prompt factory, embedding monitor) are wired in later steps.
/// </summary>
public sealed class SnapVaultModule : KoanModule
{
    public override string Id => "S6.SnapVault";

    public override void Register(IServiceCollection services)
    {
        // Bind SnapVault:Collections -> CollectionOptions (was services.Configure<>(...) in the old Program.cs).
        services.AddKoanOptions<CollectionOptions>("SnapVault:Collections");

        // NOTE: IPhotoProcessingService (consumed by PhotoProcessingJob.Execute) is intentionally NOT registered
        // yet — its implementation is port-source in _legacy/ and gets rebuilt in the jobs/domain steps (4-5).
        // No PhotoProcessingJob can be triggered before the upload surface exists, so boot stays clean.
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SnapVault");

        // ZenGarden model advisor (zero-config model selection). Reads AppHost.Current, which Program.cs sets
        // synchronously before await app.RunAsync() — so it is available by the time Start runs in host startup.
        var vision = ZenGarden.RecommendedModel(AiCapability.Vision);
        var embedding = ZenGarden.RecommendedModel(AiCapability.Embed);
        var chat = ZenGarden.RecommendedModel(AiCapability.Chat);
        logger.LogInformation(
            "ZenGarden model advisor: vision={Vision}, embedding={Embedding}, chat={Chat}",
            vision ?? "(pending)", embedding ?? "(pending)", chat ?? "(pending)");

        // Seed the [HostScoped] system analysis styles (platform-shared, un-scoped — no Tenant.Use wrapper).
        await AnalysisStyleSeeder.SeedDefaultStyles(logger);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("SnapVault", b => b.Value(
            "photo studio sample — seeds the system analysis-style library + logs the ZenGarden model advisor at boot"));
    }
}
