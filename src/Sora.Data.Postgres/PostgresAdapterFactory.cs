using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Postgres;

[ProviderPriority(14)]
public sealed class PostgresAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "npgsql", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new PostgresRepository<TEntity, TKey>(sp, opts, resolver);
    }
}