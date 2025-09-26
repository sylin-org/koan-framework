using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Postgres.Orchestration;
using Koan.Orchestration.Aspire;
using Aspire.Hosting;

namespace Koan.Data.Postgres.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "Koan.Data.Postgres";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<PostgresOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<PostgresOptions>, PostgresOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(PostgresNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, PostgresOrchestrationEvaluator>());

        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new PostgresOptions
        {
            ConnectionString = Configuration.ReadFirst(cfg, "Host=localhost;Port=5432;Database=Koan;Username=postgres;Password=postgres",
                Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            DefaultPageSize = Configuration.ReadFirst(cfg, 50,
                Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
                Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize),
            MaxPageSize = Configuration.ReadFirst(cfg, 200,
                Infrastructure.Constants.Configuration.Keys.MaxPageSize,
                Infrastructure.Constants.Configuration.Keys.AltMaxPageSize),
            SearchPath = Configuration.ReadFirst(cfg, "public",
                Infrastructure.Constants.Configuration.Keys.SearchPath)
        };
        report.AddSetting("ConnectionString", o.ConnectionString, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
        report.AddSetting("SearchPath", o.SearchPath ?? "public");
        report.AddSetting("EnsureCreatedSupported", true.ToString());
        report.AddSetting("DefaultPageSize", o.DefaultPageSize.ToString());
        report.AddSetting("MaxPageSize", o.MaxPageSize.ToString());
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
