using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Couchbase.Discovery;
using Koan.Data.Connector.Couchbase.Infrastructure;
using Koan.Data.Connector.Couchbase.Orchestration;

namespace Koan.Data.Connector.Couchbase.Initialization;

public sealed class CouchbaseAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Couchbase";
    public string? ModuleVersion => typeof(CouchbaseAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<CouchbaseOptions>();
        services.AddSingleton<IConfigureOptions<CouchbaseOptions>, CouchbaseOptionsConfigurator>();
        services.AddSingleton<CouchbaseClusterProvider>();
        services.AddSingleton<IDataAdapterFactory, CouchbaseAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CouchbaseHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Abstractions.Naming.INamingDefaultsProvider, CouchbaseNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, CouchbaseOrchestrationEvaluator>());

        // Register Couchbase discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Couchbase automatically enables Couchbase discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, CouchbaseDiscoveryAdapter>());
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from CouchbaseDiscoveryAdapter
        report.AddNote("Couchbase discovery handled by autonomous CouchbaseDiscoveryAdapter");

        // Use centralized boot reporting with adapter-specific callback
        var options = AdapterBootReporting.ConfigureForBootReportWithConfigurator<CouchbaseOptions, CouchbaseOptionsConfigurator>(
            cfg,
            (config, readiness) => new CouchbaseOptionsConfigurator(config),
            () => new CouchbaseOptions());

        report.ReportAdapterConfiguration(ModuleName, ModuleVersion, options,
            (r, o) => {
                // Couchbase-specific settings
                r.ReportConnectionString(ModuleName, "auto (resolved by discovery)");
                r.ReportStorageTargets(ModuleName, o.Bucket, o.Collection, o.Scope);
                r.ReportPerformanceSettings(ModuleName, queryTimeout: o.QueryTimeout);
                r.AddSetting($"{ModuleName}:DurabilityLevel", o.DurabilityLevel ?? "<default>");
                r.AddSetting(Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
            });
    }
}
