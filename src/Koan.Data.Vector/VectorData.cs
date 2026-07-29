using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

/// <summary>Compatibility entry points that converge on <see cref="Vector{TEntity}"/>.</summary>
public static class VectorData<TEntity> where TEntity : class, IEntity<string>
{
    public static Task Save(
        TEntity entity,
        ReadOnlyMemory<float> vector,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default) =>
        Vector<TEntity>.Save(entity, vector, metadata, ct);

    public static Task SaveWithVector(
        TEntity entity,
        ReadOnlyMemory<float> vector,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default) =>
        Vector<TEntity>.SaveWithVector(entity, vector, metadata, ct);

    public static Task<int> Save(IEnumerable<VectorEntity> items, CancellationToken ct = default) =>
        Vector<TEntity>.Save(items, ct);

    public static Task<BatchResult> SaveWithVector(
        IEnumerable<VectorEntity> items,
        CancellationToken ct = default) =>
        Vector<TEntity>.SaveWithVector(items, ct);

    public static Task<int> UpsertMany(
        IEnumerable<(string Id, float[] Embedding, object? Metadata)> items,
        CancellationToken ct = default) =>
        Vector<TEntity>.Save(items, ct);

    public static Task<VectorQueryResult<string>> Search(
        VectorQueryOptions options,
        CancellationToken ct = default) =>
        Vector<TEntity>.Search(options, ct);

    public readonly record struct VectorEntity(
        TEntity Entity,
        ReadOnlyMemory<float> Vector,
        string? Anchor = null,
        IReadOnlyDictionary<string, object>? Metadata = null);
}
