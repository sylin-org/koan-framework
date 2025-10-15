using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using ReadinessItems = Koan.Core.Adapters.AdaptersReadinessProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var defaults = new AdaptersReadinessOptions();

        var defaultPolicy = Koan.Core.Configuration.ReadWithSource(
            cfg,
            AdaptersReadinessOptions.SectionPath + ":DefaultPolicy",
            defaults.DefaultPolicy);

        var defaultTimeout = Koan.Core.Configuration.ReadWithSource(
            cfg,
            AdaptersReadinessOptions.SectionPath + ":DefaultTimeout",
            defaults.DefaultTimeout);

        var initializationTimeout = Koan.Core.Configuration.ReadWithSource(
            cfg,
            AdaptersReadinessOptions.SectionPath + ":InitializationTimeout",
            defaults.InitializationTimeout);

        var monitoring = Koan.Core.Configuration.ReadWithSource(
            cfg,
            AdaptersReadinessOptions.SectionPath + ":EnableMonitoring",
            defaults.EnableMonitoring);

        Publish(
            module,
            ReadinessItems.DefaultPolicy,
            defaultPolicy,
            displayOverride: defaultPolicy.Value.ToString());

        Publish(
            module,
            ReadinessItems.DefaultTimeout,
            defaultTimeout,
            displayOverride: defaultTimeout.Value.ToString());

        Publish(
            module,
            ReadinessItems.InitializationTimeout,
            initializationTimeout,
            displayOverride: initializationTimeout.Value.ToString());

        Publish(module, ReadinessItems.MonitoringEnabled, monitoring);
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }

    private static void Publish<T>(
        ProvenanceModuleWriter module,
        ProvenanceItem item,
        ConfigurationValue<T> value,
        object? displayOverride = null,
        ProvenancePublicationMode? modeOverride = null,
        bool? usedDefaultOverride = null,
        string? sourceKeyOverride = null,
        bool? sanitizeOverride = null)
    {
        module.AddSetting(
            item,
            modeOverride ?? ProvenanceModes.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: sourceKeyOverride ?? value.ResolvedKey,
            usedDefault: usedDefaultOverride ?? value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }
}

