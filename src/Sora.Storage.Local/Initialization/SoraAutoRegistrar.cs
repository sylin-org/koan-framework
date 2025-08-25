using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Storage;
using Sora.Storage.Abstractions;
using Sora.Storage.Local;
using Sora.Storage.Local.Infrastructure;

namespace Sora.Storage.Local.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Storage.Local";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind Local provider options and register the provider
        services.AddSoraOptions<LocalStorageOptions>(LocalStorageConstants.Configuration.Section);
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var basePath = Core.Configuration.Read(cfg, $"{LocalStorageConstants.Configuration.Section}:{LocalStorageConstants.Configuration.Keys.BasePath}", string.Empty) ?? string.Empty;
        report.AddSetting("BasePath", string.IsNullOrWhiteSpace(basePath) ? "(not set)" : basePath);
        report.AddSetting("Capabilities", "seek=true, range=true, presign=false, copy=true");
    }
}
