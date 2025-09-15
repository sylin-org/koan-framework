using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Local;
using Koan.Storage.Local.Infrastructure;

namespace Koan.Storage.Local.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Storage.Local";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind Local provider options and register the provider
        services.AddKoanOptions<LocalStorageOptions>(LocalStorageConstants.Configuration.Section);
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var basePath = Core.Configuration.Read(cfg, $"{LocalStorageConstants.Configuration.Section}:{LocalStorageConstants.Configuration.Keys.BasePath}", string.Empty) ?? string.Empty;
        report.AddSetting("BasePath", string.IsNullOrWhiteSpace(basePath) ? "(not set)" : basePath);
        report.AddSetting("Capabilities", "seek=true, range=true, presign=false, copy=true");
    }
}
