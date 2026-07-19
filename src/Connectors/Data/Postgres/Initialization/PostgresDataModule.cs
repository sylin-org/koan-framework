using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Postgres.Discovery;
using Koan.Data.Relational.Orchestration;
using PostgresItems = Koan.Data.Connector.Postgres.Infrastructure.PostgresProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Postgres.Initialization;

public sealed class PostgresDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<PostgresOptions, PostgresOptionsConfigurator>(
            Infrastructure.Constants.Configuration.Keys.Section,
            configuratorLifetime: ServiceLifetime.Singleton);
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());

        // Register PostgreSQL discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Postgres automatically enables PostgreSQL discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, PostgresDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();

        // Connection factory for Koan.Data.Direct relational sessions (DATA-0053).
        // Carried from the former manual PostgresRegistration so the auto path is complete.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, PostgresConnectionFactory>());

    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from PostgresDiscoveryAdapter
        module.AddNote("PostgreSQL discovery handled by autonomous PostgresDiscoveryAdapter");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        // Configure default options for reporting (with provenance)
        var defaultOptions = new PostgresOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        var searchPath = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);

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
            var adapter = new PostgresDiscoveryAdapter(cfg, NullLogger<PostgresDiscoveryAdapter>.Instance);
            effectiveConnectionString = ServiceDiscoveryReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => BuildPostgresFallback(defaultOptions));
        }

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            PostgresItems.ConnectionString,
            connection,
            displayOverride: effectiveConnectionString,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        module.PublishConfigValue(
            PostgresItems.SearchPath,
            searchPath,
            displayOverride: searchPath.Value ?? defaultOptions.SearchPath ?? "public");

        module.PublishConfigValue(PostgresItems.NamingStyle, namingStyle);
        module.PublishConfigValue(PostgresItems.Separator, separator);
        module.PublishConfigValue(PostgresItems.EnsureCreatedSupported, ensureCreated);
    }

    private static string BuildPostgresFallback(PostgresOptions defaults)
    {
        var database = defaults.SearchPath ?? "Koan";
        return $"Host=localhost;Port=5432;Database={database};Username=postgres;Password=postgres";
    }

}


