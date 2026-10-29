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

    // The posture every connection the connector opens carries (DATA-0158), published so an operator sees the mode in
    // effect from the startup facts and not only from the database header.
    private static readonly ProvenanceItem JournalMode = new(
        Constants.Configuration.Keys.JournalMode,
        "SQLite Journal Mode",
        "Journal mode set on every writable file database the connector opens.",
        DefaultValue: nameof(SqliteJournalMode.Wal),
        AcceptableValues: Enum.GetNames<SqliteJournalMode>(),
        DefaultConsumers: ["Koan.Data.Connector.Sqlite.Runtime.SqliteConnections"]);

    private static readonly ProvenanceItem BusyTimeout = new(
        Constants.Configuration.Keys.BusyTimeout,
        "SQLite Busy Timeout",
        "How long a connection waits for a lock another connection holds.",
        DefaultValue: "00:00:30",
        DefaultConsumers: ["Koan.Data.Connector.Sqlite.Runtime.SqliteConnections"]);

    private static readonly ProvenanceItem Synchronous = new(
        Constants.Configuration.Keys.Synchronous,
        "SQLite Synchronous",
        "Durability flush level set on every connection the connector opens.",
        DefaultValue: nameof(SqliteSynchronous.Full),
        AcceptableValues: Enum.GetNames<SqliteSynchronous>(),
        DefaultConsumers: ["Koan.Data.Connector.Sqlite.Runtime.SqliteConnections"]);

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
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Koan.Data.Abstractions.Failures.IDataFailureClassifier, SqliteFailureClassifier>());
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
        PublishPosture(module, configuration, JournalMode,
            Constants.Configuration.Keys.ProviderJournalMode, Constants.Configuration.Keys.JournalMode);
        PublishPosture(module, configuration, BusyTimeout,
            Constants.Configuration.Keys.ProviderBusyTimeout, Constants.Configuration.Keys.BusyTimeout);
        PublishPosture(module, configuration, Synchronous,
            Constants.Configuration.Keys.ProviderSynchronous, Constants.Configuration.Keys.Synchronous);
    }

    /// <summary>
    /// A posture setting reads the Default-source provider key before the connector key, as
    /// <see cref="SqliteOptionsSetup"/> does, and reports the default it falls back to.
    /// </summary>
    private static void PublishPosture(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        ProvenanceItem item,
        string providerKey,
        string connectorKey)
    {
        var scoped = Configuration.ReadWithSource<string?>(configuration, providerKey, null);
        var configured = string.IsNullOrWhiteSpace(scoped.Value)
            ? Configuration.ReadWithSource<string?>(configuration, connectorKey, item.DefaultValue)
            : scoped;
        module.PublishConfigValue(item, configured, displayOverride: configured.Value ?? item.DefaultValue);
    }
}
