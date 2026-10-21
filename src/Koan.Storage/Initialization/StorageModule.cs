using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Composition;
using Koan.Storage.Infrastructure;
using Koan.Storage.Identity;
using Koan.Storage.Options;
using Koan.Storage.Routing;
using Koan.Core.Composition;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using StorageItems = Koan.Storage.Infrastructure.StorageProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Storage.Initialization;

public sealed class StorageModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind options and register orchestrator if not already present
        services.AddKoanOptions<StorageOptions>(StorageConstants.Constants.Configuration.Section);

        // Storage realizes hard segmentation at the IStorageService boundary whoever implements the service, so the
        // realization and the chokepoint never depend on whether the application supplied one (ARCH-0137, NK-059).
        Koan.Core.Semantics.Segmentation.SegmentationParticipation.Declare(
            services,
            new Koan.Core.Semantics.Segmentation.SegmentationParticipation(
                "storage",
                typeof(StorageIdentityPlan),
                "Do not remove Koan.Storage's StorageIdentityPlan registration; an application IStorageService is wrapped by it."));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            Koan.Core.Semantics.Segmentation.ISegmentationRealization,
            StorageIdentityPlan>());
        services.TryAddSingleton(sp => sp
            .GetServices<Koan.Core.Semantics.Segmentation.ISegmentationRealization>()
            .OfType<StorageIdentityPlan>()
            .Single());

        if (!services.Any(d => d.ServiceType == typeof(IStorageService)))
        {
            services.AddSingleton<StorageProviderCatalog>();
            services.AddSingleton<StorageRoutingPlan>();
            // Expose Storage's physical-identity chokepoint around the concrete provider. With no active
            // segmentation dimensions the decorator is a byte-identical pass-through.
            services.AddSingleton<StorageService>();
            services.AddSingleton<IStorageService>(sp => new ScopedStorageService(
                sp.GetRequiredService<StorageService>(),
                sp.GetRequiredService<StorageIdentityPlan>()));
        }
        else
        {
            // An application-supplied IStorageService stays authoritative for mechanics and routing; the chokepoint
            // still composes the segmentation particles onto every key it receives.
            WrapSuppliedStorageServices(services);
        }
    }

    private static void WrapSuppliedStorageServices(IServiceCollection services)
    {
        for (var index = 0; index < services.Count; index++)
        {
            var supplied = services[index];
            if (supplied.ServiceType != typeof(IStorageService) || supplied.IsKeyedService) continue;

            services[index] = ServiceDescriptor.Describe(
                typeof(IStorageService),
                sp => new ScopedStorageService(Resolve(sp, supplied), sp.GetRequiredService<StorageIdentityPlan>()),
                supplied.Lifetime);
        }

        static IStorageService Resolve(IServiceProvider sp, ServiceDescriptor supplied)
            => (IStorageService)(supplied.ImplementationInstance
                ?? supplied.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, supplied.ImplementationType!));
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        // A Storage reference makes the capability available. Configuration makes routing active;
        // an actual service resolution remains the fail-loud boundary for unconfigured runtime use.
        // A deliberately supplied IStorageService remains authoritative for routing and needs no routing plan.
        if (services.GetRequiredService<IOptions<StorageOptions>>().Value.DeclaresRoutingIntent)
            _ = services.GetService<StorageRoutingPlan>();
        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var profilesPath = $"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.Profiles}";
        var profilesSection = cfg.GetSection(profilesPath);
        var profilesExists = profilesSection.Exists();
        var profilesCount = profilesExists ? profilesSection.GetChildren().Count() : 0;
        var profilesMode = ProvenanceModes.FromBootSource(
            profilesExists ? BootSettingSource.AppSettings : BootSettingSource.Auto,
            usedDefault: !profilesExists);

        module.AddSetting(
            StorageItems.Profiles,
            profilesMode,
            profilesCount,
            sourceKey: profilesPath,
            usedDefault: !profilesExists);

        var defaultProfile = Core.Configuration.ReadWithSource(
            cfg,
            $"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.DefaultProfile}",
            "");

        module.AddSetting(
            StorageItems.DefaultProfile,
            ProvenanceModes.FromConfigurationValue(defaultProfile),
            string.IsNullOrWhiteSpace(defaultProfile.Value) ? null : defaultProfile.Value,
            sourceKey: defaultProfile.ResolvedKey,
            usedDefault: defaultProfile.UsedDefault);

    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => StorageCompositionFacts.Project(composition, services, Id);
}

