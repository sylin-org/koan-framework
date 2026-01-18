using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Json.Strict.Extensions;
using Koan.Web.Json.Strict.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Json.Strict.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Web.Json.Strict";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Log.BootDebug("registrar.init", "strict-json-loading", ("module", ModuleName));

        services.AddKoanOptions<KoanMinimalJsonOptions>(Constants.Configuration.Section);
        services.AddKoanMinimalJsonStrict();

        Log.BootDebug("registrar.init", "strict-json-registered", ("module", ModuleName));
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configuration);

        module.Describe(ModuleVersion);

        var settings = new KoanMinimalJsonOptions();
        configuration.GetSection(Constants.Configuration.Section).Bind(settings);

        module.AddSetting(
            "strict",
            settings.Strict ? "enabled" : "disabled",
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.Strict}",
            state: settings.Strict ? ProvenanceSettingState.Configured : ProvenanceSettingState.Default);

        module.AddSetting(
            "allow-duplicate-properties",
            settings.AllowDuplicateProperties.ToString(),
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.AllowDuplicateProperties}");

        module.AddSetting(
            "combine-registered-resolvers",
            settings.CombineRegisteredResolvers.ToString(),
            source: BootSettingSource.AppSettings,
            sourceKey: $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.CombineRegisteredResolvers}");
    }
}
