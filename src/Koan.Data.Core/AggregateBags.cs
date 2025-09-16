using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public static class AggregateBags
{
    public static TBag GetOrAdd<TEntity, TKey, TBag>(IServiceProvider sp, string key, Func<TBag> factory)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        where TBag : class
    {
        var cfg = AggregateConfigs.Get<TEntity, TKey>(sp);
        return cfg.GetOrAddBag(key, factory);
    }
}