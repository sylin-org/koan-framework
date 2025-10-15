using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Json.Infrastructure;

namespace Koan.Data.Connector.Json.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Json";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options from config and register adapter + health contributor
        services.AddKoanOptions<JsonDataOptions>();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<JsonDataOptions>, JsonDataOptionsConfigurator>();
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, JsonHealthContributor>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var defaultOptions = new JsonDataOptions();

        var directory = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DirectoryPath,
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DirectoryPath}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DirectoryPath}");

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DefaultPageSize}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DefaultPageSize}");

        var maxPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.MaxPageSize}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.MaxPageSize}");

        module.AddSetting(
            Constants.Bootstrap.DirectoryPath,
            directory.Value,
            source: directory.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Json.JsonDataOptionsConfigurator",
                "Koan.Data.Connector.Json.JsonAdapterFactory"
            },
            sourceKey: directory.ResolvedKey);

        module.AddSetting(
            Constants.Bootstrap.DefaultPageSize,
            defaultPageSize.Value.ToString(),
            source: defaultPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Json.JsonAdapterFactory"
            },
            sourceKey: defaultPageSize.ResolvedKey);

        module.AddSetting(
            Constants.Bootstrap.MaxPageSize,
            maxPageSize.Value.ToString(),
            source: maxPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Json.JsonAdapterFactory"
            },
            sourceKey: maxPageSize.ResolvedKey);
    }
}


