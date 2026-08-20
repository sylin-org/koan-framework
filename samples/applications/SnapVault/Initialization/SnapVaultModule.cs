using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Hosting;
using Koan.Web.Context;
using SnapVault.Configuration;
using SnapVault.Services;

namespace SnapVault.Initialization;

/// <summary>
/// Owns SnapVault's configuration, domain services, access posture, seed data, and boot report.
/// </summary>
public sealed class SnapVaultModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CollectionOptions>("SnapVault:Collections");

        // Studio-to-client lifecycle: explicit grant, proof, and integrity-checked deprovisioning.
        services.AddSingleton<GalleryGrantService>();
        services.AddSingleton<ProofingService>();
        services.AddSingleton<SnapVaultDeprovisioningService>();

        // Durable ingest and optional enrichment.
        services.AddSingleton<Services.AI.AnalysisPromptFactory>();
        services.AddSingleton<PhotoProcessingService>();

        // Session-windowed gallery queries.
        services.AddSingleton<PhotoSetService>();

        // One request contributor validates gallery links and contributes their tenant/read context.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWebContextContributor, SnapVaultContextContributor>());

        // Structural blob cleanup belongs to host composition, not mutable process state.
        PhotoAssetCleanup.Register();
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SnapVault");

        await AnalysisStyleSeeder.SeedDefaultStyles(logger);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("SnapVault", b => b.Value(
            "local-first photo studio with durable ingest, media recipes, client proofing, and optional AI/vector enrichment"));
    }
}
