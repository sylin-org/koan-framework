
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Connector.OpenSearch.Discovery;

namespace Koan.Data.Connector.OpenSearch.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.OpenSearch";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<OpenSearchOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<OpenSearchOptions>, OpenSearchOptionsConfigurator>();

        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, OpenSearchNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OpenSearchHealthContributor>());

        // Register OpenSearch discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.OpenSearch automatically enables OpenSearch discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OpenSearchDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, OpenSearchVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from OpenSearchDiscoveryAdapter
        report.AddNote("OpenSearch discovery handled by autonomous OpenSearchDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new OpenSearchOptions();

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("Endpoint", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("IndexPrefix", defaultOptions.IndexPrefix ?? "koan");
        report.AddSetting("VectorField", defaultOptions.VectorField);
        report.AddSetting("MetadataField", defaultOptions.MetadataField);
        report.AddSetting("SimilarityMetric", defaultOptions.SimilarityMetric);
        report.AddSetting("TimeoutSeconds", defaultOptions.DefaultTimeoutSeconds.ToString());
    }
}

