using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Media.Web.Routing;
using Koan.Web.Hosting;
using SnapVault.Configuration;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Initialization;

/// <summary>
/// Owns SnapVault's configuration, domain services, media source, access posture, seed data, and boot report.
/// </summary>
public sealed class SnapVaultModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CollectionOptions>("SnapVault:Collections");

        // Studio-to-client lifecycle: invite, grant, proof, and verifiable deprovisioning.
        services.AddSingleton<GalleryInviteService>();
        services.AddSingleton<GuestScopeService>();
        services.AddSingleton<ProofingService>();
        services.AddSingleton<SnapVaultDeprovisioningService>();

        // MediaEntitySource resolves through PhotoAsset, so tenant and guest scopes protect rendered bytes too.
        services.AddMediaSource<PhotoAsset>();

        // Durable ingest and optional enrichment.
        services.AddSingleton<Services.AI.AnalysisPromptFactory>();
        services.AddSingleton<PhotoProcessingService>();

        // Session-windowed gallery queries.
        services.AddSingleton<PhotoSetService>();

        // Every request receives an explicit guest/operator subject; missing subjects remain fail-closed.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, SnapVaultSubjectContributor>());

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
