using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging.Abstractions;

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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from CouchbaseDiscoveryAdapter
        module.AddNote("Couchbase discovery handled by autonomous CouchbaseDiscoveryAdapter");

        // Use centralized boot reporting with adapter-specific callback
        var options = AdapterBootReporting.ConfigureForBootReportWithConfigurator<CouchbaseOptions, CouchbaseOptionsConfigurator>(
            cfg,
            (config, readiness) => new CouchbaseOptionsConfigurator(config),
            () => new CouchbaseOptions());

        module.ReportAdapterConfiguration(ModuleName, ModuleVersion, options,
            (m, o) => {
                // Couchbase-specific settings
                var connectionParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(o.Bucket)) connectionParameters["bucket"] = o.Bucket!;
                if (!string.IsNullOrWhiteSpace(o.Username)) connectionParameters["username"] = o.Username!;
                if (!string.IsNullOrWhiteSpace(o.Password)) connectionParameters["password"] = o.Password!;

                var connectionString = ResolveCouchbaseConnectionString(cfg, o.ConnectionString, connectionParameters);
                m.ReportConnectionString(ModuleName, connectionString);
                m.ReportStorageTargets(ModuleName, o.Bucket, o.Collection, o.Scope);
                m.ReportPerformanceSettings(ModuleName, queryTimeout: o.QueryTimeout);
                m.AddSetting($"{ModuleName}:DurabilityLevel", o.DurabilityLevel ?? "<default>");
                m.AddSetting(Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
            });
    }

    private static string ResolveCouchbaseConnectionString(
        IConfiguration configuration,
        string? configuredConnection,
        IDictionary<string, object> parameters)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnection) &&
            !string.Equals(configuredConnection, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return configuredConnection!;
        }

        var adapter = new CouchbaseDiscoveryAdapter(configuration, NullLogger<CouchbaseDiscoveryAdapter>.Instance);
        return AdapterBootReporting.ResolveConnectionString(
            configuration,
            adapter,
            parameters,
            () => "couchbase://localhost");
    }
}

