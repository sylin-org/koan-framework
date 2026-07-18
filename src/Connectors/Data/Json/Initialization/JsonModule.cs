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

public sealed class JsonModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind options from config and register adapter + health contributor
        services.AddKoanOptions<JsonDataOptions>();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<JsonDataOptions>, JsonDataOptionsConfigurator>();
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, JsonHealthContributor>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");
        var defaultOptions = new JsonDataOptions();

        var directory = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DirectoryPath,
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DirectoryPath}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DirectoryPath}");

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
    }
}


