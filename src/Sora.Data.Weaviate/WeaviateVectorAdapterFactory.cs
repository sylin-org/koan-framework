using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Weaviate;

[ProviderPriority(10)]
public sealed class WeaviateVectorAdapterFactory : IVectorAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "weaviate", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<WeaviateOptions>?)sp.GetService(typeof(IOptions<WeaviateOptions>))
            ?? throw new InvalidOperationException("WeaviateOptions not configured; bind Sora:Data:Weaviate.");
        return new WeaviateVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }
}