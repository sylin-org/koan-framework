using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Messaging.Core.Pillars;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("MessagingCore.Enabled", "true");
        report.AddSetting("InMemoryProvider.Available", "true");
    }
}