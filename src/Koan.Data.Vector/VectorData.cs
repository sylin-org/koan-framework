using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

public static class VectorData<TEntity>
    where TEntity : class, IEntity<string>
{
    private static IVectorSearchRepository<TEntity, string> Repo
    => (Koan.Core.Hosting.App.AppHost.Current?.GetService<IVectorService>()?.TryGetRepository<TEntity, string>())
           ?? throw new InvalidOperationException("No vector adapter configured for this entity.");

    public static Task<int> UpsertManyAsync(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
        => Repo.UpsertManyAsync(items, ct);

    public static Task<VectorQueryResult<string>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
        => Repo.SearchAsync(options, ct);
}