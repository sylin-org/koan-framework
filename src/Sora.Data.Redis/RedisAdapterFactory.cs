using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using StackExchange.Redis;
using Sora.Orchestration;

namespace Sora.Data.Redis;

[ProviderPriority(5)]
[DefaultEndpoint("redis", "redis", 6379, "tcp", "redis", UriPattern = "redis://{host}:{port}")]
[HostMount("/data")]
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