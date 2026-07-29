using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Json.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Json.Initialization;

public sealed class JsonModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<JsonDataOptions>(Infrastructure.Constants.Configuration.Section);
        services.TryAddSingleton<JsonFileRegistry>();
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, JsonHealthContributor>());
    }

    public override void Report(
        Koan.Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped");
        var directory = Configuration.ReadFirstWithSource(
            configuration,
            new JsonDataOptions().DirectoryPath,
            $"{Infrastructure.Constants.Configuration.Section}:{Infrastructure.Constants.Configuration.DirectoryPath}",
            $"{Infrastructure.Constants.Configuration.DefaultSourceSection}:{Infrastructure.Constants.Configuration.DirectoryPath}");
        module.AddSetting(
            Infrastructure.Constants.Bootstrap.DirectoryPath,
            directory.Value,
            source: directory.Source,
            consumers: ["Koan.Data.Connector.Json.Runtime.JsonRoute"],
            sourceKey: directory.ResolvedKey);
    }
}
