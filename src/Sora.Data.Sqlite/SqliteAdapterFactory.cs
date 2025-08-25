using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Sqlite;

[ProviderPriority(10)]
public sealed class SqliteAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<SqliteOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new SqliteRepository<TEntity, TKey>(sp, opts, resolver);
    }
}