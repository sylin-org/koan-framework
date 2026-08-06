using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite.Initialization;

public sealed class SqliteModule : KoanModule
{
    private static readonly ProvenanceItem ConnectionString = new(
        Constants.Configuration.Keys.ConnectionString,
        "SQLite Connection String",
        "SQLite file or memory connection used by the adapter.",
        MustSanitize: true,
        DefaultValue: "auto",
        DefaultConsumers: ["Koan.Data.Connector.Sqlite.SqliteAdapterFactory"]);

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqliteOptions>();
        services.AddSingleton<IConfigureOptions<SqliteOptions>, SqliteOptionsSetup>();
        services.TryAddSingleton<SqliteConnections>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<SqliteAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<SqliteAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<SqliteAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataProviderConnectionFactory, SqliteConnectionFactory>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("SQLite provides one relational execution path for managed and externally mapped sources.");
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
