using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Storage;
using Sora.Storage.Options;
using Sora.Storage.Infrastructure;

namespace Sora.Storage.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Storage";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options and register orchestrator if not already present
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageConstants.Constants.Configuration.Section)
            .ValidateDataAnnotations();

        if (!services.Any(d => d.ServiceType == typeof(IStorageService)))
        {
            services.AddSingleton<IStorageService, StorageService>();
        }
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    var defaultProfile = Core.Configuration.Read(cfg, StorageConstants.Constants.Configuration.Keys.DefaultProfile, string.Empty) ?? string.Empty;
    var fallback = Core.Configuration.Read(cfg, StorageConstants.Constants.Configuration.Keys.FallbackMode, nameof(StorageFallbackMode.SingleProfileOnly));
    var validate = Core.Configuration.Read(cfg, StorageConstants.Constants.Configuration.Keys.ValidateOnStart, true);
        // Profiles are a complex object; report count when available
    var profilesSection = cfg.GetSection($"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.Profiles}");
        var profilesCount = profilesSection.Exists() ? profilesSection.GetChildren().Count() : 0;
        report.AddSetting("Profiles", profilesCount.ToString());
        if (!string.IsNullOrWhiteSpace(defaultProfile)) report.AddSetting("DefaultProfile", defaultProfile);
        report.AddSetting("FallbackMode", fallback);
        report.AddSetting("ValidateOnStart", validate.ToString());
    }
}
