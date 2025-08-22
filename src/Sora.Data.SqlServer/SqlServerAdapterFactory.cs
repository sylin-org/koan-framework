using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.SqlServer;

[ProviderPriority(15)]
public sealed class SqlServerAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "mssql", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new SqlServerRepository<TEntity, TKey>(sp, opts, resolver);
    }
}