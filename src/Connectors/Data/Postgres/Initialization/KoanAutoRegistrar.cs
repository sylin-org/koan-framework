using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Postgres.Discovery;
using Koan.Data.Connector.Postgres.Orchestration;
using Koan.Data.Relational.Orchestration;
using Koan.Orchestration.Aspire;
using Aspire.Hosting;

namespace Koan.Data.Connector.Postgres.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Postgres";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<PostgresOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<PostgresOptions>, PostgresOptionsConfigurator>();
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

        var connectionValue = string.IsNullOrWhiteSpace(connection.Value)
            ? "auto"
            : connection.Value;
        var connectionIsAuto = string.Equals(connectionValue, "auto", StringComparison.OrdinalIgnoreCase);

        module.AddSetting(
            "ConnectionString",
            connectionIsAuto ? "auto (resolved by discovery)" : connectionValue,
            isSecret: !connectionIsAuto,
            source: connection.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresOptionsConfigurator",
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory",
                "Koan.Data.Connector.Postgres.Initialization.KoanAutoRegistrar"
            },
            sourceKey: connection.ResolvedKey);

        module.AddSetting(
            "NamingStyle",
            defaultOptions.NamingStyle.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.NamingStyle);

        module.AddSetting(
            "Separator",
            defaultOptions.Separator,
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.Separator);

        module.AddSetting(
            "SearchPath",
            (searchPath.Value ?? "public").ToString(),
            source: searchPath.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresOptionsConfigurator",
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
            },
            sourceKey: searchPath.ResolvedKey);

        module.AddSetting(
            "EnsureCreatedSupported",
            true.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported);

        module.AddSetting(
            "DefaultPageSize",
            defaultPageSize.Value.ToString(),
            source: defaultPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
            },
            sourceKey: defaultPageSize.ResolvedKey);

        module.AddSetting(
            "MaxPageSize",
            maxPageSize.Value.ToString(),
            source: maxPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
            },
            sourceKey: maxPageSize.ResolvedKey);
    }

    // IKoanAspireRegistrar implementation
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new PostgresOptions();
        new PostgresOptionsConfigurator(configuration).Configure(options);

        // Parse connection string to extract database name, username, and password
        var connectionParts = ParseConnectionString(options.ConnectionString);

        var postgres = builder.AddPostgres("postgres", port: connectionParts.Port)
            .WithDataVolume()
            .WithEnvironment("POSTGRES_DB", connectionParts.Database ?? "Koan")
            .WithEnvironment("POSTGRES_USER", connectionParts.Username ?? "postgres");

        // Only set password if one is provided and not empty
        if (!string.IsNullOrEmpty(connectionParts.Password))
        {
            postgres.WithEnvironment("POSTGRES_PASSWORD", connectionParts.Password);
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

    private (string? Database, string? Username, string? Password, int Port) ParseConnectionString(string connectionString)
    {
        // Simple connection string parsing for PostgreSQL
        // Format: "Host=localhost;Port=5432;Database=Koan;Username=postgres;Password=postgres"
        var parts = connectionString.Split(';');
        string? database = null;
        string? username = null;
        string? password = null;
        int port = 5432;

        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2)
            {
                var key = keyValue[0].Trim().ToLowerInvariant();
                var value = keyValue[1].Trim();

                switch (key)
                {
                    case "database":
                        database = value;
                        break;
                    case "username":
                    case "user id":
                    case "userid":
                    case "uid":
                        username = value;
                        break;
                    case "password":
                    case "pwd":
                        password = value;
                        break;
                    case "port":
                        if (int.TryParse(value, out var parsedPort))
                            port = parsedPort;
                        break;
                }
            }
        }

        return (database, username, password, port);
    }
}


