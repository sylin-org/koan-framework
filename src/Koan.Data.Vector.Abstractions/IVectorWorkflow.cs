using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

public interface IVectorWorkflow<TEntity>
    where TEntity : class, IEntity<string>
{
    string Profile { get; }
    bool IsAvailable { get; }

    Task Save(TEntity entity, float[] embedding, object? metadata = null, CancellationToken ct = default);

    Task<VectorWorkflowSaveManyResult> SaveMany(
        IEnumerable<(TEntity Entity, float[] Embedding, object? Metadata)> items,
        CancellationToken ct = default);

    Task<bool> Delete(string id, CancellationToken ct = default);

    Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default);

    Task EnsureCreated(CancellationToken ct = default);

    Task<VectorQueryResult<string>> Query(VectorQueryOptions options, CancellationToken ct = default);

    Task<VectorQueryResult<string>> Query(
        float[] vector,
        string? text = null,
        int? topK = null,
        double? alpha = null,
        object? filter = null,
        string? vectorName = null,
        CancellationToken ct = default);
}
