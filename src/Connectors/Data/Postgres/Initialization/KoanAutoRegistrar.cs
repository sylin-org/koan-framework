using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Postgres.Discovery;
using Koan.Data.Connector.Postgres.Orchestration;
using Koan.Data.Relational.Orchestration;
using Koan.Orchestration.Aspire;
using Aspire.Hosting;
using PostgresItems = Koan.Data.Connector.Postgres.Infrastructure.PostgresProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Postgres.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Postgres";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<PostgresOptions, PostgresOptionsConfigurator>(
            Infrastructure.Constants.Configuration.Keys.Section,
            configuratorLifetime: ServiceLifetime.Singleton);
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(PostgresNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<RelationalMaterializationOptions>, PostgresRelationalMaterializationOptionsConfigurator>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, PostgresOrchestrationEvaluator>());

        // Register PostgreSQL discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Postgres automatically enables PostgreSQL discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, PostgresDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from PostgresDiscoveryAdapter
        module.AddNote("PostgreSQL discovery handled by autonomous PostgresDiscoveryAdapter");

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

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);

        var maxPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

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
            effectiveConnectionString = AdapterBootReporting.ResolveConnectionString(
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
        module.PublishConfigValue(PostgresItems.DefaultPageSize, defaultPageSize);
        module.PublishConfigValue(PostgresItems.MaxPageSize, maxPageSize);
    }

    private static string BuildPostgresFallback(PostgresOptions defaults)
    {
        var database = defaults.SearchPath ?? "Koan";
        return $"Host=localhost;Port=5432;Database={database};Username=postgres;Password=postgres";
    }

    // IKoanAspireRegistrar implementation
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new PostgresOptions();
        new PostgresOptionsConfigurator(configuration).Configure(options);

        // ARCH-0068: Use static ConnectionStringParser for unified parsing
        var components = Koan.Core.Orchestration.ConnectionStringParser.Parse(
            options.ConnectionString,
            "postgres");

        var postgres = builder.AddPostgres("postgres", port: components.Port)
            .WithDataVolume()
            .WithEnvironment("POSTGRES_DB", components.Database ?? "Koan")
            .WithEnvironment("POSTGRES_USER", components.Username ?? "postgres");

        // Only set password if one is provided and not empty
        if (!string.IsNullOrEmpty(components.Password))
        {
            postgres.WithEnvironment("POSTGRES_PASSWORD", components.Password);
        }

        // TODO: Configure proper health check for PostgreSQL
        // postgres.WithHealthCheck("/health"); // This pattern doesn't work for database resources
    }

    public int Priority => 100; // Infrastructure resources register early

    public bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment)
    {
        // Register in development environments automatically (Reference = Intent)
        // or when explicitly configured in other environments
        return environment.IsDevelopment() || HasExplicitConfiguration(configuration);
    }

    private bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check if there's explicit Postgres configuration
        return !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString]) ||
               !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.AltConnectionString]) ||
               !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres]);
    }

}


