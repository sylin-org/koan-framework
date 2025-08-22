using Sora.Data.Abstractions;

namespace Sora.Data.Vector.Abstractions;

public interface IVectorSearchRepository<TEntity, TKey> where TEntity : IEntity<TKey> where TKey : notnull
{
    Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default);
    Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);
    Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default);

    Task VectorEnsureCreatedAsync(CancellationToken ct = default) => Task.CompletedTask; // optional convenience
    Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default);
}