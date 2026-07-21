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
        // Compatibility surface, one execution owner: AggregateConfig no longer constructs a parallel,
        // decorator-free repository graph. IDataService owns routing, decoration and the outer Data facade.
        _repo = new Lazy<IDataRepository<TEntity, TKey>>(
            () => sp.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
