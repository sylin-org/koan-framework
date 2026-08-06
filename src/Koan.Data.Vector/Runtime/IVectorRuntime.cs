using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

internal interface IVectorRuntime
{
    VectorExecution<TEntity, TKey> Resolve<TEntity, TKey>(DataOperationEffect effect, string operation)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}

internal sealed record VectorExecution<TEntity, TKey>(
    VectorSpacePlan Plan,
    DataSourcePlan Source,
    IVectorSearchRepository<TEntity, TKey> Repository,
    VectorMetadataMaterializer Metadata)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull;
