using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public sealed class AggregateConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public string Provider { get; }
    public AggregateMetadata.IdSpec? Id { get; }

    private readonly Lazy<IDataRepository<TEntity, TKey>> _repo;
    public IDataRepository<TEntity, TKey> Repository => _repo.Value;

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
            var guards = sp.GetServices<Pipeline.IStorageGuard>().ToArray();
            return new RepositoryFacade<TEntity, TKey>(repo, guards);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
