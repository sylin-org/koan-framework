using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public sealed class AggregateConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;

    public string Provider => AdapterResolver.ResolveDecisionForEntity<TEntity>(
        _services,
        _services.GetRequiredService<DataSourceRegistry>()).Adapter;
    public AggregateMetadata.IdSpec? Id { get; }

    public IDataRepository<TEntity, TKey> Repository =>
        _services.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();

    internal AggregateConfig(AggregateMetadata.IdSpec? id, IServiceProvider sp)
    {
        _services = sp;
        Id = id;
    }
}
