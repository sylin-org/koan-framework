using System;

namespace Sora.Data.Abstractions;

public interface IDataAdapterFactory
{
    bool CanHandle(string provider);
    IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
