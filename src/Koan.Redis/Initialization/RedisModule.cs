using Aspire.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Services;
using Koan.Orchestration.Aspire;
using Koan.Redis.Connections;
using Koan.Redis.Discovery;
using Koan.Redis.Options;
using Koan.Redis.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Redis.Initialization;

[KoanService(ServiceKind.Other, "redis", "Redis",
    Subtype = "shared-backend",
    ContainerImage = "redis",
    DefaultTag = "7",
    DefaultPorts = [6379],
    Capabilities = ["protocol=redis"],
    Volumes = ["./Data/redis:/data"],
    AppEnv = ["ConnectionStrings__Redis={host}:{port}"],
    Scheme = "redis",
    Host = "redis",
    EndpointPort = 6379,
    UriPattern = "redis://{host}:{port}",
    LocalScheme = "redis",
    LocalHost = "localhost",
    LocalPort = 6379,
    LocalPattern = "redis://{host}:{port}")]
public sealed class RedisModule : KoanModule, IKoanAspireResources
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<RedisOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, RedisDiscoveryAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, RedisOrchestrationEvaluator>());

        services.AddSingleton<RedisConnectionProvider>();
        services.AddSingleton<IRedisConnectionProvider>(static services =>
            services.GetRequiredService<RedisConnectionProvider>());
        services.AddSingleton<IConnectionMultiplexer>(static services =>
            services.GetRequiredService<RedisConnectionProvider>().GetDefaultForContainer());
    }

    public override void Report(
        Koan.Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        var connection = Configuration.ReadFirstWithSource(
            configuration,
            "auto",
            Infrastructure.Constants.Configuration.StandardConnectionString,
            Infrastructure.Constants.Configuration.ConnectionString,
            Infrastructure.Constants.Discovery.RedisUrl,
            Infrastructure.Constants.Discovery.RedisConnectionString);
        module.PublishConfigValue(Infrastructure.RedisProvenanceItems.ConnectionString, connection);
        module.AddNote("One host-owned connection is shared per distinct Redis endpoint.");
    }

    public void RegisterAspireResources(
        IDistributedApplicationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);
        var components = ConnectionStringParser.Parse(options.ConnectionString, "redis");
        var redis = builder.AddRedis("redis", port: components.Port).WithDataVolume();
        if (!string.IsNullOrWhiteSpace(components.Password))
            redis.WithEnvironment("REDIS_PASSWORD", components.Password);
    }

    public int Priority => 200;

    public bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment)
        => environment.IsDevelopment() || RedisOptionsConfigurator.HasExplicitConnection(configuration);
}
