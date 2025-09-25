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
using Koan.Data.Abstractions;
using Koan.Data.Couchbase.Infrastructure;
using Koan.Data.Couchbase.Orchestration;

namespace Koan.Data.Couchbase.Initialization;

public sealed class CouchbaseAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Couchbase";
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
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        // Use centralized boot reporting with adapter-specific callback
        var options = AdapterBootReporting.ConfigureForBootReportWithConfigurator<CouchbaseOptions, CouchbaseOptionsConfigurator>(
            cfg,
            (config, readiness) => new CouchbaseOptionsConfigurator(config, null, readiness),
            () => new CouchbaseOptions());

        report.ReportAdapterConfiguration(ModuleName, ModuleVersion, options,
            (r, o) => {
                // Couchbase-specific settings
                r.ReportConnectionString(ModuleName, o.ConnectionString);
                r.ReportStorageTargets(ModuleName, o.Bucket, o.Collection, o.Scope);
                r.ReportPerformanceSettings(ModuleName, queryTimeout: o.QueryTimeout);
                r.AddSetting($"{ModuleName}:DurabilityLevel", o.DurabilityLevel ?? "<default>");
                r.AddSetting(Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
            });
    }
}
