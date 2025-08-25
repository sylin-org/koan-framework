using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;

namespace Sora.Data.Core;

internal sealed class AggregateConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public string Provider { get; }
    public AggregateMetadata.IdSpec? Id { get; }

    private readonly Lazy<IDataRepository<TEntity, TKey>> _repo;
    public IDataRepository<TEntity, TKey> Repository => _repo.Value;

    // Optional per-entity cache for provider/dialect-specific rendered commands and binders
    private readonly ConcurrentDictionary<string, object> _bags = new();
    public TBag GetOrAddBag<TBag>(string key, Func<TBag> factory) where TBag : class
        => (TBag)_bags.GetOrAdd(key, _ => factory()!);
    internal IEnumerable<(string key, object value)> EnumerateBags()
        => _bags.Select(kvp => (kvp.Key, kvp.Value));

    internal AggregateConfig(string provider, AggregateMetadata.IdSpec? id, IServiceProvider sp)
    {
        Provider = provider;
        Id = id;
        _repo = new Lazy<IDataRepository<TEntity, TKey>>(() =>
        {
            var factories = sp.GetServices<IDataAdapterFactory>();
            var factory = factories.FirstOrDefault(f => f.CanHandle(provider))
                          ?? throw new InvalidOperationException($"No data adapter factory for provider '{provider}'");
            var repo = factory.Create<TEntity, TKey>(sp);
            var manager = sp.GetRequiredService<IAggregateIdentityManager>();
            // Decorate with RepositoryFacade for cross-cutting concerns
            return new RepositoryFacade<TEntity, TKey>(repo, manager);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}