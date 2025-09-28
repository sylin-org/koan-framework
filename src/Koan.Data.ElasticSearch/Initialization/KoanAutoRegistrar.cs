
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.ElasticSearch.Discovery;

namespace Koan.Data.ElasticSearch.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.ElasticSearch";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<ElasticSearchOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<ElasticSearchOptions>, ElasticSearchOptionsConfigurator>();

        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Abstractions.Naming.INamingDefaultsProvider, ElasticSearchNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, ElasticSearchHealthContributor>());

        // Register ElasticSearch discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.ElasticSearch automatically enables ElasticSearch discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, ElasticSearchDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, ElasticSearchVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from ElasticSearchDiscoveryAdapter
        report.AddNote("ElasticSearch discovery handled by autonomous ElasticSearchDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new ElasticSearchOptions();

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("Endpoint", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("IndexPrefix", defaultOptions.IndexPrefix ?? "koan");
        report.AddSetting("VectorField", defaultOptions.VectorField);
        report.AddSetting("MetadataField", defaultOptions.MetadataField);
        report.AddSetting("SimilarityMetric", defaultOptions.SimilarityMetric);
        report.AddSetting("TimeoutSeconds", defaultOptions.DefaultTimeoutSeconds.ToString());
    }
}
