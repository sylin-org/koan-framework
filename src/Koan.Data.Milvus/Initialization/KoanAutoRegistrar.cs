
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
using Koan.Data.Milvus.Discovery;

namespace Koan.Data.Milvus.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Milvus";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<MilvusOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<MilvusOptions>, MilvusOptionsConfigurator>();

        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Abstractions.Naming.INamingDefaultsProvider, MilvusNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MilvusHealthContributor>());

        // Register Milvus discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Milvus automatically enables Milvus discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MilvusDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, MilvusVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from MilvusDiscoveryAdapter
        report.AddNote("Milvus discovery handled by autonomous MilvusDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new MilvusOptions();

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("Endpoint", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("DatabaseName", defaultOptions.DatabaseName);
        report.AddSetting("VectorFieldName", defaultOptions.VectorFieldName);
        report.AddSetting("MetadataFieldName", defaultOptions.MetadataFieldName);
        report.AddSetting("Metric", defaultOptions.Metric);
        report.AddSetting("ConsistencyLevel", defaultOptions.ConsistencyLevel);
        report.AddSetting("TimeoutSeconds", defaultOptions.DefaultTimeoutSeconds.ToString());
        report.AddSetting("AutoCreateCollection", defaultOptions.AutoCreateCollection.ToString());
    }
}
