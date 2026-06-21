using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Vector adapter factory for sqlite-vec. <see cref="CanHandle"/> answers to <c>sqlite</c> so it auto-pairs
/// with the SQLite data adapter (the standard vector election derives the desired provider from the data
/// provider) — referencing both gives you durable, co-located vectors with zero config. Also answers to
/// <c>sqlitevec</c>/<c>sqlite-vec</c> for an explicit <c>[VectorAdapter]</c> choice.
/// </summary>
[ProviderPriority(40)]
public sealed class SqliteVecAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "sqlitevec";

    public bool CanHandle(string provider)
        => string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlitevec", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlite-vec", StringComparison.OrdinalIgnoreCase);

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '_',
            Partition = new PartitionTokenPolicy { GuidFormat = "D", AllowedExtraChars = "_" },
        };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var options = sp.GetService<IOptions<SqliteVecOptions>>()?.Value ?? new SqliteVecOptions();
        return new SqliteVecVectorRepository<TEntity, TKey>(this, sp, options);
    }
}
