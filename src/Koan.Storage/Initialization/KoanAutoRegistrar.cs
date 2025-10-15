using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Infrastructure;
using Koan.Storage.Options;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using StorageItems = Koan.Storage.Infrastructure.StorageProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Storage.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Storage";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options and register orchestrator if not already present
        services.AddKoanOptions<StorageOptions>(StorageConstants.Constants.Configuration.Section);

        if (!services.Any(d => d.ServiceType == typeof(IStorageService)))
        {
            services.AddSingleton<IStorageService, StorageService>();
        }
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

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
            string.Empty);

        module.AddSetting(
            StorageItems.DefaultProfile,
            ProvenanceModes.FromConfigurationValue(defaultProfile),
            string.IsNullOrWhiteSpace(defaultProfile.Value) ? null : defaultProfile.Value,
            sourceKey: defaultProfile.ResolvedKey,
            usedDefault: defaultProfile.UsedDefault);

        var fallback = Core.Configuration.ReadWithSource(
            cfg,
            $"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.FallbackMode}",
            StorageFallbackMode.SingleProfileOnly);

        module.AddSetting(
            StorageItems.FallbackMode,
            ProvenanceModes.FromConfigurationValue(fallback),
            fallback.Value,
            sourceKey: fallback.ResolvedKey,
            usedDefault: fallback.UsedDefault);

        var validate = Core.Configuration.ReadWithSource(
            cfg,
            $"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.ValidateOnStart}",
            true);

        module.AddSetting(
            StorageItems.ValidateOnStart,
            ProvenanceModes.FromConfigurationValue(validate),
            validate.Value,
            sourceKey: validate.ResolvedKey,
            usedDefault: validate.UsedDefault);
    }
}

