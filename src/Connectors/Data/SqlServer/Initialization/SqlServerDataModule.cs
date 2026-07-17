using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.SqlServer.Discovery;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlServerItems = Koan.Data.Connector.SqlServer.Infrastructure.SqlServerProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.SqlServer.Initialization;

public sealed class SqlServerDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqlServerOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());

        // Bridge SQL Server provider options into the relational materialization pipeline.
        // Carried from the former manual SqlServerRegistration so the auto path is complete.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqlServerToRelationalBridgeConfigurator>());

        // Register SQL Server discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.SqlServer automatically enables SQL Server discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, SqlServerDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();

        // Connection factory for Koan.Data.Direct relational sessions (DATA-0053).
        // Carried from the former manual SqlServerRegistration so the auto path is complete.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, SqlServerConnectionFactory>());

        // Register the relational schema orchestrator (required by SqlServerRepository).
        // The auto-discovery path needs it, or schema bootstrap fails. Mirrors Sqlite/Postgres.
        services.AddRelationalOrchestration();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from SqlServerDiscoveryAdapter
        module.AddNote("SQL Server discovery handled by autonomous SqlServerDiscoveryAdapter");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new SqlServerOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);

        var namingStyle = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.NamingStyle,
            Infrastructure.Constants.Configuration.Keys.NamingStyle,
            Infrastructure.Constants.Configuration.Keys.AltNamingStyle);

        var separator = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Separator,
            Infrastructure.Constants.Configuration.Keys.Separator,
            Infrastructure.Constants.Configuration.Keys.AltSeparator);

        var ensureCreated = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported,
            true);

    var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSourceKey = connection.ResolvedKey ?? Infrastructure.Constants.Configuration.Keys.ConnectionString;

        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new SqlServerDiscoveryAdapter(cfg, NullLogger<SqlServerDiscoveryAdapter>.Instance);
            effectiveConnectionString = ServiceDiscoveryReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => BuildSqlServerFallback());
        }

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            SqlServerItems.ConnectionString,
            connection,
            displayOverride: effectiveConnectionString,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        module.PublishConfigValue(SqlServerItems.NamingStyle, namingStyle);
        module.PublishConfigValue(SqlServerItems.Separator, separator);
        module.PublishConfigValue(SqlServerItems.EnsureCreatedSupported, ensureCreated);
        module.PublishConfigValue(SqlServerItems.DefaultPageSize, defaultPageSize);
    }

    private static string BuildSqlServerFallback()
        => "Server=localhost;Database=Koan;Trusted_Connection=True;Encrypt=False";
}

