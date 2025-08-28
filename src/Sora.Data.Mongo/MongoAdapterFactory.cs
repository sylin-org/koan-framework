using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

namespace Sora.Data.Mongo;

[ProviderPriority(20)]
[SoraService(ServiceKind.Database, shortCode: "mongo", name: "MongoDB",
    ContainerImage = "mongo",
    DefaultTag = "7",
    DefaultPorts = new[] { 27017 },
    Capabilities = new[] { "protocol=mongodb" },
    Volumes = new[] { "./Data/mongo:/data/db" },
    AppEnv = new[] { "Sora__Data__Mongo__ConnectionString={scheme}://{host}:{port}", "Sora__Data__Mongo__Database=sora" },
    Scheme = "mongodb", Host = "mongo", EndpointPort = 27017, UriPattern = "mongodb://{host}:{port}",
    LocalScheme = "mongodb", LocalHost = "localhost", LocalPort = 27017, LocalPattern = "mongodb://{host}:{port}")]
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
