using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;

namespace Koan.Core.Adapters.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Core.Adapters.Readiness";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("Koan.Core.Adapters.Initialization.KoanAutoRegistrar");
        logger?.Log(LogLevel.Debug, "Koan.Core.Adapters KoanAutoRegistrar loaded.");

        services.AddKoanOptions<AdaptersReadinessOptions>(AdaptersReadinessOptions.SectionPath);
        services.TryAddSingleton<IRetryPolicyProvider, DefaultRetryPolicyProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AdapterInitializationService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AdapterReadinessMonitor>());

        logger?.Log(LogLevel.Debug, "Koan.Core.Adapters services registered successfully.");
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var section = cfg.GetSection(AdaptersReadinessOptions.SectionPath);
        report.AddSetting("Adapters.Readiness:DefaultPolicy", section["DefaultPolicy"] ?? ReadinessPolicy.Hold.ToString());
        report.AddSetting("Adapters.Readiness:DefaultTimeout", section["DefaultTimeout"] ?? TimeSpan.FromSeconds(30).ToString());
        report.AddSetting("Adapters.Readiness:InitializationTimeout", section["InitializationTimeout"] ?? TimeSpan.FromMinutes(5).ToString());
        report.AddSetting("Adapters.Readiness:Monitoring", section["EnableMonitoring"] ?? bool.TrueString);
    }
}
