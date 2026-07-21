using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Complete vector adapter contract: vector-repository creation. Discovery, naming and source-routing come from
/// <see cref="IAdapterFactory"/> (ARCH-0103 §4.1 — the marker base shared with <see cref="IDataAdapterFactory"/>);
/// this adds the vector <see cref="Create{TEntity,TKey}"/>.
/// </summary>
/// <remarks>
/// The <c>source</c> parameter (new in ARCH-0103 P1) aligns the vector <c>Create</c> with the record
/// <see cref="IDataAdapterFactory.Create{TEntity,TKey}"/> and closes the vector/record routing <b>split-brain</b>:
/// <c>VectorService</c> now resolves the routed source through the shared <c>RoutedSource</c> (the same decision the
/// record plane makes) and passes it here, so a Database-mode <c>[DataAxis]</c> routes the embedding to the same
/// physical store as the row. Adapters that do not yet realize per-source physical placement accept the parameter and
/// ignore it (their realization lands in ARCH-0103 P4); InMemoryVector and SqliteVec realize it in P1.
/// </remarks>
public interface IVectorAdapterFactory : IAdapterFactory
{
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
