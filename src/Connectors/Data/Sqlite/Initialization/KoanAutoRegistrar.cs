using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Data.Connector.Sqlite";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));
        services.AddKoanOptions<SqliteOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqliteNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());

        // Register SQLite discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Sqlite automatically enables SQLite discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Discovery.SqliteDiscoveryAdapter>());

        // Ensure relational orchestration services are available (schema validation/creation)
        services.AddRelationalOrchestration();
        // Bridge SQLite governance options into relational orchestrator options
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqliteToRelationalBridgeConfigurator>());

        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from SqliteDiscoveryAdapter
        report.AddNote("SQLite discovery handled by autonomous SqliteDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new SqliteOptions();

        var connection = Koan.Core.Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlite,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        var defaultPageSize = Koan.Core.Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);

        var maxPageSize = Koan.Core.Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        var connectionValue = string.IsNullOrWhiteSpace(connection.Value)
            ? "auto"
            : connection.Value;
        var connectionIsAuto = string.Equals(connectionValue, "auto", StringComparison.OrdinalIgnoreCase);

        var bootOptions = AdapterBootReporting.ConfigureForBootReportWithConfigurator<SqliteOptions, SqliteOptionsConfigurator>(
            cfg,
            (configuration, readiness) => new SqliteOptionsConfigurator(configuration),
            static () => new SqliteOptions());

        var resolvedConnectionString = connectionIsAuto ? bootOptions.ConnectionString : connectionValue;
        if (string.IsNullOrWhiteSpace(resolvedConnectionString))
        {
            resolvedConnectionString = connectionValue;
        }

        report.AddSetting(
            "ConnectionString",
            resolvedConnectionString ?? "auto",
            isSecret: true,
            source: connection.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Sqlite.SqliteOptionsConfigurator",
                "Koan.Data.Connector.Sqlite.SqliteAdapterFactory"
            },
            sourceKey: connection.ResolvedKey);

        report.AddSetting(
            "NamingStyle",
            defaultOptions.NamingStyle.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Sqlite.SqliteAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.NamingStyle);

        report.AddSetting(
            "Separator",
            defaultOptions.Separator,
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Sqlite.SqliteAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.Separator);

        report.AddSetting(
            Infrastructure.Constants.Bootstrap.EnsureCreatedSupported,
            true.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Sqlite.SqliteAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported);

        report.AddSetting(
            Infrastructure.Constants.Bootstrap.DefaultPageSize,
            defaultPageSize.Value.ToString(),
            source: defaultPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Sqlite.SqliteAdapterFactory"
            },
            sourceKey: defaultPageSize.ResolvedKey);

        report.AddSetting(
            Infrastructure.Constants.Bootstrap.MaxPageSize,
            maxPageSize.Value.ToString(),
            source: maxPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Sqlite.SqliteAdapterFactory"
            },
            sourceKey: maxPageSize.ResolvedKey);
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}

