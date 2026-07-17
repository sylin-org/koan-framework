using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Connector.Local;
using Koan.Storage.Connector.Local.Infrastructure;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Storage.Connector.Local.Initialization;

public sealed class LocalStorageModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind Local provider options and register the provider
        services.AddKoanOptions<LocalStorageOptions>(LocalStorageConstants.Configuration.Section);
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var basePath = Core.Configuration.Read(cfg, $"{LocalStorageConstants.Configuration.Section}:{LocalStorageConstants.Configuration.Keys.BasePath}", "") ?? "";
        module.AddSetting("BasePath", string.IsNullOrWhiteSpace(basePath) ? "(not set)" : basePath);
        module.AddSetting("Capabilities", "seek=true, range=true, presign=false, copy=true");
    }
}


