using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Reporting;
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

public sealed class CouchbaseDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CouchbaseOptions>();
        services.AddSingleton<IConfigureOptions<CouchbaseOptions>, CouchbaseOptionsConfigurator>();
        services.AddSingleton<CouchbaseClusterProvider>();
        services.AddSingleton<IDataAdapterFactory, CouchbaseAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CouchbaseHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, CouchbaseOrchestrationEvaluator>());

        // Register Couchbase discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Couchbase automatically enables Couchbase discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, CouchbaseDiscoveryAdapter>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from CouchbaseDiscoveryAdapter
        module.AddNote("Couchbase discovery handled by autonomous CouchbaseDiscoveryAdapter");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        // Use centralized boot reporting with adapter-specific callback
        var options = AdapterBootReporting.ConfigureForBootReportWithConfigurator<CouchbaseOptions, CouchbaseOptionsConfigurator>(
            cfg,
            (config, readiness) => new CouchbaseOptionsConfigurator(config),
            () => new CouchbaseOptions());

        module.ReportAdapterConfiguration(Id, Version, options,
            (m, o) => {
                // Couchbase-specific settings
                var connectionParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(o.Bucket)) connectionParameters["bucket"] = o.Bucket!;
                if (!string.IsNullOrWhiteSpace(o.Username)) connectionParameters["username"] = o.Username!;
                if (!string.IsNullOrWhiteSpace(o.Password)) connectionParameters["password"] = o.Password!;

                var connectionString = ResolveCouchbaseConnectionString(cfg, o.ConnectionString, connectionParameters);
                m.ReportConnectionString(Id, connectionString);
                m.ReportStorageTargets(Id, o.Bucket, o.Collection, o.Scope);
                m.ReportPerformanceSettings(Id, queryTimeout: o.QueryTimeout);
                m.AddSetting($"{Id}:DurabilityLevel", o.DurabilityLevel ?? "<default>");
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
        return ServiceDiscoveryReporting.ResolveConnectionString(
            configuration,
            adapter,
            parameters,
            () => "couchbase://localhost");
    }
}

