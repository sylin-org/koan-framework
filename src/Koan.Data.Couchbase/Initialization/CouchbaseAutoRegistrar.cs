using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
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
        report.AddModule(ModuleName, ModuleVersion);
        var configurator = new CouchbaseOptionsConfigurator(cfg, null);
        var options = new CouchbaseOptions();
        configurator.Configure(options);
        report.AddSetting("Bucket", options.Bucket);
        report.AddSetting("Scope", options.Scope ?? "_default");
        report.AddSetting("Collection", options.Collection ?? "<convention>");
        report.AddSetting("ConnectionString", Redaction.DeIdentify(options.ConnectionString), isSecret: true);
        report.AddSetting(Constants.Bootstrap.DefaultPageSize, options.DefaultPageSize.ToString(CultureInfo.InvariantCulture));
        report.AddSetting(Constants.Bootstrap.MaxPageSize, options.MaxPageSize.ToString(CultureInfo.InvariantCulture));
        report.AddSetting(Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
    }
}
