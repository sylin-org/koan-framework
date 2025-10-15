using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Redis.Orchestration;
using StackExchange.Redis;
using Koan.Orchestration.Aspire;
using Aspire.Hosting;

namespace Koan.Data.Connector.Redis.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Redis";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<RedisOptions>();
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RedisHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, RedisOrchestrationEvaluator>());

        // Register Redis discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Redis automatically enables Redis discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Discovery.RedisDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, RedisAdapterFactory>();

        // Only register connection multiplexer if Redis is available or in Aspire context
        RegisterConnectionMultiplexer(services);
    }

    private void RegisterConnectionMultiplexer(IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var cs = cfg.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                cs = KoanEnv.InContainer ? Infrastructure.Constants.Discovery.DefaultCompose : Infrastructure.Constants.Discovery.DefaultLocal;
            }

            var logger = sp.GetService<ILogger<KoanAutoRegistrar>>();
            logger?.LogDebug("Attempting Redis connection to: {ConnectionString}", cs);
            try
            {
                return ConnectionMultiplexer.Connect(cs);
            }
            catch (RedisConnectionException ex)
            {
                logger?.LogError(ex, "Redis connection failed: {Message}", ex.Message);
                throw new InvalidOperationException($"Redis is not available. Connection string: {cs}. " +
                    "Ensure Redis is running or use the Aspire AppHost for managed Redis.", ex);
            }
        });
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from RedisDiscoveryAdapter
        module.AddNote("Redis discovery handled by autonomous RedisDiscoveryAdapter");

        // Configure default options for reporting (with provenance)
        var defaultOptions = new RedisOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}",
            "ConnectionStrings:Redis",
            "ConnectionStrings:Default");

        var database = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Database,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.Database}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.Database}");

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.DefaultPageSize}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.DefaultPageSize}");

        var maxPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.MaxPageSize}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.MaxPageSize}");

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
                "Koan.Data.Connector.Redis.RedisOptionsConfigurator",
                "Koan.Data.Connector.Redis.RedisAdapterFactory",
                "Koan.Data.Connector.Redis.Initialization.KoanAutoRegistrar"
            },
            sourceKey: connection.ResolvedKey);

        module.AddSetting(
            "Database",
            database.Value.ToString(),
            source: database.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Redis.RedisOptionsConfigurator",
                "StackExchange.Redis.ConnectionMultiplexer"
            },
            sourceKey: database.ResolvedKey);

        module.AddSetting(
            Infrastructure.Constants.Bootstrap.EnsureCreatedSupported,
            true.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Redis.RedisAdapterFactory"
            },
            sourceKey: $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported}");

        module.AddSetting(
            Infrastructure.Constants.Bootstrap.DefaultPageSize,
            defaultPageSize.Value.ToString(),
            source: defaultPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Redis.RedisAdapterFactory"
            },
            sourceKey: defaultPageSize.ResolvedKey);

        module.AddSetting(
            Infrastructure.Constants.Bootstrap.MaxPageSize,
            maxPageSize.Value.ToString(),
            source: maxPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Redis.RedisAdapterFactory"
            },
            sourceKey: maxPageSize.ResolvedKey);
    }

    // IKoanAspireRegistrar implementation
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);

        // Parse connection string to extract port and password if provided
        var connectionParts = ParseRedisConnectionString(options.ConnectionString);

        var redis = builder.AddRedis("redis", port: connectionParts.Port)
            .WithDataVolume();

        // Set password if one is provided and not empty
        if (!string.IsNullOrEmpty(connectionParts.Password))
        {
            redis.WithEnvironment("REDIS_PASSWORD", connectionParts.Password);
        }

        // Set default database if not 0
        if (options.Database != 0)
        {
            redis.WithEnvironment("REDIS_DEFAULT_DB", options.Database.ToString());
        }

        // TODO: Configure proper health check for Redis
        // redis.WithHealthCheck("/health");
    }

    public int Priority => 200; // Cache infrastructure registers after databases but before apps

    public bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment)
    {
        // Register in development environments or when explicitly configured
        return environment.IsDevelopment() || HasExplicitConfiguration(configuration);
    }

    private bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check if there's explicit Redis configuration
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);

        return !string.IsNullOrEmpty(options.ConnectionString) ||
               !string.IsNullOrEmpty(configuration["Redis:ConnectionString"]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:Redis"]);
    }

    private (int Port, string? Password) ParseRedisConnectionString(string? connectionString)
    {
        // Default values
        int port = 6379;
        string? password = null;

        if (string.IsNullOrEmpty(connectionString))
        {
            return (port, password);
        }

        // Redis connection string formats:
        // "localhost:6379"
        // "localhost:6379,password=mypassword"
        // "redis://localhost:6379"
        // "redis://:password@localhost:6379"

        try
        {
            // Handle redis:// URL format
            if (connectionString.StartsWith("redis://"))
            {
                var uri = new Uri(connectionString);
                port = uri.Port != -1 ? uri.Port : 6379;

                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var userInfo = uri.UserInfo.Split(':');
                    if (userInfo.Length > 1)
                        password = userInfo[1];
                }

                return (port, password);
            }

            // Handle comma-separated options format
            var parts = connectionString.Split(',');
            var hostPort = parts[0].Trim();

            // Extract port from host:port
            var hostPortParts = hostPort.Split(':');
            if (hostPortParts.Length > 1 && int.TryParse(hostPortParts[1], out var parsedPort))
            {
                port = parsedPort;
            }

            // Look for password in options
            foreach (var part in parts.Skip(1))
            {
                var option = part.Trim();
                if (option.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                {
                    password = option.Substring(9);
                }
            }
        }
        catch
        {
            // If parsing fails, use defaults
        }

        return (port, password);
    }
}


