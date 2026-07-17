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
using Koan.Data.Connector.Cockroach.Discovery;
using Koan.Data.Connector.Cockroach.Orchestration;
using Koan.Data.Relational.Orchestration;
using CockroachItems = Koan.Data.Connector.Cockroach.Infrastructure.CockroachProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Cockroach.Initialization;

// CockroachDB rides the PostgreSQL wire protocol (Npgsql) and the PostgreSQL SQL dialect — this adapter is a thin
// delta over the shipped Postgres connector (ARCH-0094 blueprint §2.4 reuse). Aspire orchestration is intentionally
// dropped here (no Aspire.Hosting Cockroach resource provider ships); Reference = Intent registration + discovery is
// sufficient for the conformance gate.
public sealed class CockroachDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CockroachOptions, CockroachOptionsConfigurator>(
            Infrastructure.Constants.Configuration.Keys.Section,
            configuratorLifetime: ServiceLifetime.Singleton);
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CockroachHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<RelationalMaterializationOptions>, CockroachRelationalMaterializationOptionsConfigurator>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, CockroachOrchestrationEvaluator>());

        // Register CockroachDB discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Cockroach automatically enables CockroachDB discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, CockroachDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, CockroachAdapterFactory>();

        // Connection factory for Koan.Data.Direct relational sessions (DATA-0053).
        // Carried from the former manual CockroachRegistration so the auto path is complete.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, CockroachConnectionFactory>());

        // Register the relational schema orchestrator (required by CockroachRepository.EnsureOrchestrated).
        // The auto-discovery path needs it, or schema bootstrap fails with "No service for type
        // IRelationalSchemaOrchestrator has been registered." Mirrors what Sqlite and SqlServer do.
        services.AddRelationalOrchestration();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from CockroachDiscoveryAdapter
        module.AddNote("CockroachDB discovery handled by autonomous CockroachDiscoveryAdapter");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        // Configure default options for reporting (with provenance)
        var defaultOptions = new CockroachOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsCockroach,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        var searchPath = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);

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
            var adapter = new CockroachDiscoveryAdapter(cfg, NullLogger<CockroachDiscoveryAdapter>.Instance);
            effectiveConnectionString = ServiceDiscoveryReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => BuildCockroachFallback(defaultOptions));
        }

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            CockroachItems.ConnectionString,
            connection,
            displayOverride: effectiveConnectionString,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        module.PublishConfigValue(
            CockroachItems.SearchPath,
            searchPath,
            displayOverride: searchPath.Value ?? defaultOptions.SearchPath ?? "public");

        module.PublishConfigValue(CockroachItems.NamingStyle, namingStyle);
        module.PublishConfigValue(CockroachItems.Separator, separator);
        module.PublishConfigValue(CockroachItems.EnsureCreatedSupported, ensureCreated);
        module.PublishConfigValue(CockroachItems.DefaultPageSize, defaultPageSize);
    }

    private static string BuildCockroachFallback(CockroachOptions defaults)
    {
        var database = defaults.SearchPath ?? "Koan";
        return $"Host=localhost;Port=26257;Database={database};Username=root";
    }
}
