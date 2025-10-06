using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from RedisDiscoveryAdapter
        report.AddNote("Redis discovery handled by autonomous RedisDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new RedisOptions();
        var database = Koan.Core.Configuration.Read(cfg, "Koan:Data:Redis:Database", defaultOptions.Database);

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("Database", database.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.DefaultPageSize, defaultOptions.DefaultPageSize.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.MaxPageSize, defaultOptions.MaxPageSize.ToString());
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

