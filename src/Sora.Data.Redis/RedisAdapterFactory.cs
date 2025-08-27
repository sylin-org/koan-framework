using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using StackExchange.Redis;
using Sora.Orchestration;
using Sora.Orchestration.Abstractions.Attributes;

namespace Sora.Data.Redis;

[ProviderPriority(5)]
[ServiceId("redis")]
[ContainerDefaults(
    image: "redis",
    Tag = "7",
    Ports = new[] { 6379 },
    Volumes = new[] { "./Data/redis:/data" }
)]
[EndpointDefaults(EndpointMode.Container, scheme: "redis", host: "redis", port: 6379, UriPattern = "redis://{host}:{port}")]
[EndpointDefaults(EndpointMode.Local, scheme: "redis", host: "localhost", port: 6379, UriPattern = "redis://{host}:{port}")]
[AppEnvDefaults(
    "Sora__Data__Redis__Endpoint={scheme}://{host}:{port}"
)]
public sealed class RedisAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "redis", StringComparison.OrdinalIgnoreCase);
    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<RedisOptions>>();
        var muxer = sp.GetRequiredService<IConnectionMultiplexer>();
        return new RedisRepository<TEntity, TKey>(opts, muxer, sp.GetService<ILoggerFactory>());
    }
}