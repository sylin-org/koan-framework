using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Sse.Infrastructure;
using Koan.Web.Sse.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Sse.Initialization;

public sealed class SseModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddKoanOptions<KoanSseOptions>(Constants.Configuration.Section);
        services.TryAddSingleton(typeof(SseHub<>));
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configuration);

        module.Describe(Version);
        var options = new KoanSseOptions();
        configuration.GetSection(Constants.Configuration.Section).Bind(options);
        var state = options.DefaultEvent == KoanSseOptions.DefaultEventName
            ? ProvenanceSettingState.Default
            : ProvenanceSettingState.Configured;

        module.AddSetting(
            "default-event",
            options.DefaultEvent,
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.DefaultEvent}",
            state: state);

        module.AddSetting(
            "heartbeat",
            options.Heartbeat > TimeSpan.Zero ? options.Heartbeat.ToString() : "off",
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.Heartbeat}",
            state: options.Heartbeat == KoanSseOptions.DefaultHeartbeat
                ? ProvenanceSettingState.Default
                : ProvenanceSettingState.Configured);

        module.AddNote(
            "Sse.Stream available; request cancellation, per-frame flush and heartbeat enabled; SseHub<T> fans out within this process; replay belongs to the application");
    }
}
