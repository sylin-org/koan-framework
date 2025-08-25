using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Vector;

public static class VectorData<TEntity>
    where TEntity : class, IEntity<string>
{
    private static IVectorSearchRepository<TEntity, string> Repo
        => (Sora.Core.SoraApp.Current?.GetService<IVectorService>()?.TryGetRepository<TEntity, string>())
           ?? throw new InvalidOperationException("No vector adapter configured for this entity.");

    public static Task<int> UpsertManyAsync(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
        => Repo.UpsertManyAsync(items, ct);

    public static Task<VectorQueryResult<string>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
        => Repo.SearchAsync(options, ct);
}