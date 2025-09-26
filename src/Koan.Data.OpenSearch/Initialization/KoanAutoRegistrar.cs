
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.OpenSearch.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.OpenSearch";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<OpenSearchOptions>(Infrastructure.Constants.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenSearchOptions>, OpenSearchOptionsConfigurator>());

        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Abstractions.Naming.INamingDefaultsProvider, OpenSearchNamingDefaultsProvider>());

        services.AddSingleton<IVectorAdapterFactory, OpenSearchVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OpenSearchHealthContributor>());

        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Configuration.Read(cfg, $"{Infrastructure.Constants.Section}:Endpoint", "http://localhost:9200");
        report.AddSetting("OpenSearch:Endpoint", endpoint, isSecret: false);
        report.AddSetting("OpenSearch:IndexPrefix", Configuration.Read(cfg, $"{Infrastructure.Constants.Section}:IndexPrefix", "koan"), isSecret: false);
    }
}
