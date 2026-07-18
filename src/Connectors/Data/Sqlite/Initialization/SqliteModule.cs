using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Relational.Orchestration;
using Koan.Core.Provenance;
using SqliteItems = Koan.Data.Connector.Sqlite.Infrastructure.SqliteProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Sqlite.Initialization;

public sealed class SqliteModule : KoanModule
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<SqliteModule>();

    public override void Register(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", Id));
        services.AddKoanOptions<SqliteOptions, SqliteOptionsConfigurator>(
            Infrastructure.Constants.Configuration.Keys.Section,
            configuratorLifetime: ServiceLifetime.Singleton);
        services.TryAddSingleton<SqliteConnectionLifecycle>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());

        // Register SQLite discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Sqlite automatically enables SQLite discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Discovery.SqliteDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();

        // Connection factory for Koan.Data.Direct relational sessions (DATA-0053).
        // Carried from the former manual SqliteRegistration so the auto path is complete.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, SqliteConnectionFactory>());

        Log.BootDebug(LogActions.Init, "services-registered", ("module", Id));
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Runtime connection resolution supports autonomous discovery with a local .koan/data/Koan.sqlite fallback");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new SqliteOptions();

        var connection = ResolveDefaultConnectionForReport(cfg, defaultOptions);

        var namingStyle = Koan.Core.Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.NamingStyle,
            Infrastructure.Constants.Configuration.Keys.NamingStyle,
            Infrastructure.Constants.Configuration.Keys.AltNamingStyle);

        var separator = Koan.Core.Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Separator,
            Infrastructure.Constants.Configuration.Keys.Separator,
            Infrastructure.Constants.Configuration.Keys.AltSeparator);

        var ensureCreated = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported,
            true);

        var connectionValue = string.IsNullOrWhiteSpace(connection.Value)
            ? "auto"
            : connection.Value;
        var connectionIsAuto = string.Equals(connectionValue, "auto", StringComparison.OrdinalIgnoreCase);

        // Provenance is descriptive and side-effect free. Runtime discovery and directory creation happen only when
        // the elected adapter is actually used; an unresolved target is therefore reported honestly as "auto".
        var displayConnection = connectionIsAuto || string.IsNullOrWhiteSpace(connectionValue)
            ? "auto"
            : connectionValue;

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            SqliteItems.ConnectionString,
            connection,
            displayOverride: displayConnection,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connection.ResolvedKey ?? Infrastructure.Constants.Configuration.Keys.ConnectionString,
            sanitizeOverride: connectionIsAuto ? false : null);

        module.PublishConfigValue(SqliteItems.NamingStyle, namingStyle);
        module.PublishConfigValue(SqliteItems.Separator, separator);
        module.PublishConfigValue(SqliteItems.EnsureCreatedSupported, ensureCreated);
    }

    private static ConfigurationValue<string> ResolveDefaultConnectionForReport(
        IConfiguration cfg,
        SqliteOptions defaults)
    {
        var fallback = Infrastructure.SqliteConnectionConfiguration
            .ReadProviderFallbackWithSource(cfg, defaults.ConnectionString);

        var registry = new DataSourceRegistry();
        registry.DiscoverFromConfiguration(cfg);
        var defaultSource = registry.GetSource("Default");
        if (!string.IsNullOrWhiteSpace(defaultSource?.Adapter) &&
            !string.Equals(defaultSource.Adapter, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            cfg,
            registry,
            "sqlite",
            "Default",
            fallback.Value);

        var genericSource = Koan.Core.Configuration.ReadWithSource<string?>(
            cfg,
            Infrastructure.Constants.Configuration.Keys.DefaultSourceConnectionString,
            null);
        if (!genericSource.UsedDefault && IsConcrete(genericSource.Value))
        {
            return From(genericSource, resolved);
        }

        // An explicit generic "auto" delegates to the adapter configurator. Otherwise a concrete provider-scoped
        // source is authoritative over the adapter/global fallback, exactly as the runtime resolver applies it.
        var genericRequestsAuto = !genericSource.UsedDefault && IsAuto(genericSource.Value);
        var providerSource = Koan.Core.Configuration.ReadWithSource<string?>(
            cfg,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            null);
        if (!genericRequestsAuto && !providerSource.UsedDefault && IsConcrete(providerSource.Value))
        {
            return From(providerSource, resolved);
        }

        if (genericRequestsAuto && IsAuto(resolved) && fallback.UsedDefault)
        {
            return From(genericSource, resolved);
        }

        return new ConfigurationValue<string>(
            resolved,
            fallback.Source,
            fallback.ResolvedKey,
            fallback.UsedDefault);
    }

    private static ConfigurationValue<string> From(ConfigurationValue<string?> source, string resolved)
        => new(resolved, source.Source, source.ResolvedKey, source.UsedDefault);

    private static bool IsConcrete(string? value) => !string.IsNullOrWhiteSpace(value) && !IsAuto(value);
    private static bool IsAuto(string? value) => Infrastructure.SqliteConnectionConfiguration.IsAuto(value);

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}


