
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.ElasticSearch.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.ElasticSearch";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var loggerFactory = services.BuildServiceProvider().GetService<ILoggerFactory>();
        loggerFactory?.CreateLogger("Koan.Data.ElasticSearch.Initialization").LogDebug("Elasticsearch adapter registrar loaded");

        services.AddKoanOptions<ElasticSearchOptions>(Infrastructure.Constants.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<ElasticSearchOptions>, ElasticSearchOptionsConfigurator>());

        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Abstractions.Naming.INamingDefaultsProvider, ElasticSearchNamingDefaultsProvider>());

        services.AddSingleton<IVectorAdapterFactory, ElasticSearchVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, ElasticSearchHealthContributor>());

        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Configuration.Read(cfg, $"{Infrastructure.Constants.Section}:Endpoint", "http://localhost:9200");
        report.AddSetting("ElasticSearch:Endpoint", endpoint, isSecret: false);
        report.AddSetting("ElasticSearch:IndexPrefix", Configuration.Read(cfg, $"{Infrastructure.Constants.Section}:IndexPrefix", "koan"), isSecret: false);
    }
}
