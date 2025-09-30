using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;

namespace Koan.Core.Adapters.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Core.Adapters.Readiness";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

        services.AddKoanOptions<AdaptersReadinessOptions>(AdaptersReadinessOptions.SectionPath);
        services.TryAddSingleton<IRetryPolicyProvider, DefaultRetryPolicyProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AdapterInitializationService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AdapterReadinessMonitor>());

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
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

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}
