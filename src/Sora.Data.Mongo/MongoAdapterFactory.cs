using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration;
using Sora.Orchestration.Abstractions.Attributes;

namespace Sora.Data.Mongo;

[ProviderPriority(20)]
[ServiceId("mongo")]
[ContainerDefaults("mongo", Tag = "7", Ports = new[] { 27017 }, Volumes = new[] { "./Data/mongo:/data/db" })]
[EndpointDefaults(EndpointMode.Container, scheme: "mongodb", host: "mongo", port: 27017, UriPattern = "mongodb://{host}:{port}")]
[EndpointDefaults(EndpointMode.Local, scheme: "mongodb", host: "localhost", port: 27017, UriPattern = "mongodb://{host}:{port}")]
[AppEnvDefaults(
    "Sora__Data__Mongo__ConnectionString={scheme}://{host}:{port}",
    "Sora__Data__Mongo__Database=sora"
)]
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