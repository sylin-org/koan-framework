using System;
using System.Globalization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Provenance;
using Koan.Core.Modules;
using Koan.Web.Sse.Infrastructure;
using Koan.Web.Sse.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Sse.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Web.Sse";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

        services.AddKoanOptions<KoanSseOptions>(Constants.Configuration.Section);

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configuration);

        module.Describe(ModuleVersion);
        var optionsSection = configuration.GetSection(Constants.Configuration.Section);
        var options = new KoanSseOptions();
        optionsSection.Bind(options);

        var defaultEventState = options.DefaultEvent == KoanSseOptions.DefaultEventName
            ? ProvenanceSettingState.Default
            : ProvenanceSettingState.Configured;

        module.AddSetting(
            "default-event",
            options.DefaultEvent,
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.DefaultEvent}",
            state: defaultEventState);

        module.AddSetting(
            "heartbeat-seconds",
            ((int)options.HeartbeatInterval.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.HeartbeatInterval}");
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}
