using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration;

namespace Sora.Data.Mongo;

[ProviderPriority(20)]
[DefaultEndpoint("mongodb", "mongo", 27017, "tcp", "mongo", "mongodb", UriPattern = "mongodb://{host}:{port}")]
[HostMount("/data/db")]
public sealed class MongoAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "mongo", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "mongodb", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp) where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new MongoRepository<TEntity, TKey>(opts, resolver, sp);
    }
}