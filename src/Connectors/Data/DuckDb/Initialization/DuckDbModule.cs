using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Analytics;
using Koan.Data.Abstractions.Analytics;
using Koan.Data.Connector.DuckDb.Infrastructure;
using Koan.Data.Connector.DuckDb.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.DuckDb.Initialization;

public sealed class DuckDbModule : KoanModule
{
    private static readonly ProvenanceItem ConnectionString = new(
        Constants.Configuration.Keys.ConnectionString,
        "DuckDB Connection String",
        "DuckDB file or memory connection used by the adapter.",
        MustSanitize: true,
        DefaultValue: "auto",
        DefaultConsumers: ["Koan.Data.Connector.DuckDb.DuckDbAdapterFactory"]);

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<DuckDbOptions>();
        services.AddSingleton<IConfigureOptions<DuckDbOptions>, DuckDbOptionsSetup>();
        services.TryAddSingleton<DuckDbConnections>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<DuckDbAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<DuckDbAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<DuckDbAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, DuckDbHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataProviderConnectionFactory, DuckDbConnectionFactory>());
        services.AddSingleton<IAnalyticsEngine>(DuckDbAnalyticsEngine.Instance);
        services.AddSingleton<IAnalyticsProjectionSink, DuckDbAnalyticsProjectionSink>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("DuckDB provides one in-process analytical execution path for managed and externally mapped sources.");
        var configured = Configuration.ReadWithSource<string?>(
            configuration,
            Constants.Configuration.Keys.ConnectionString,
            null);
        var automatic = string.IsNullOrWhiteSpace(configured.Value) ||
                        string.Equals(configured.Value, "auto", StringComparison.OrdinalIgnoreCase);
        module.PublishConfigValue(
            ConnectionString,
            configured,
            displayOverride: automatic ? "auto" : configured.Value,
            modeOverride: automatic
                ? ProvenancePublicationModeExtensions.FromBootSource(BootSettingSource.Auto, usedDefault: true)
                : ProvenancePublicationModeExtensions.FromConfigurationValue(configured),
            usedDefaultOverride: automatic,
            sourceKeyOverride: configured.ResolvedKey ?? Constants.Configuration.Keys.ConnectionString,
            sanitizeOverride: !automatic);
    }
}
