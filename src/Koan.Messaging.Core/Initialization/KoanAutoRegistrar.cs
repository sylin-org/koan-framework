using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Messaging.Core.Pillars;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Messaging.Core.Initialization;

/// <summary>
/// Auto-registers the core Koan messaging services when Koan.Messaging.Core is referenced.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Messaging.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        MessagingPillarManifest.EnsureRegistered();
        // Register core messaging services
        services.AddKoanMessaging();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting(
            "MessagingCore.Enabled",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Messaging.Core.Runtime" });
        module.AddSetting(
            "InMemoryProvider.Available",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Messaging.Core.InMemory" });
    }
}
