using System;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.WebSockets.Extensions;
using Koan.WebSockets.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.WebSockets.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.WebSockets";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

        services.AddWebSocketStreamAdapters();

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configuration);

        module.Describe(ModuleVersion);

        var options = new WebSocketStreamOptions();
        configuration.GetSection(Constants.Configuration.Section).Bind(options);

        var defaultOptions = WebSocketStreamOptions.Default;

        module.AddSetting(
            "message-type",
            options.MessageType.ToString(),
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.MessageType}",
            state: options.MessageType == defaultOptions.MessageType
                ? ProvenanceSettingState.Default
                : ProvenanceSettingState.Configured);

        module.AddSetting(
            "leave-open",
            options.LeaveOpen.ToString(),
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.LeaveOpen}",
            state: options.LeaveOpen
                ? ProvenanceSettingState.Configured
                : ProvenanceSettingState.Default);

        module.AddSetting(
            "sub-protocol",
            string.IsNullOrWhiteSpace(options.SubProtocol) ? "(not-set)" : options.SubProtocol,
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.SubProtocol}",
            state: string.IsNullOrWhiteSpace(options.SubProtocol)
                ? ProvenanceSettingState.Default
                : ProvenanceSettingState.Configured);
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}
