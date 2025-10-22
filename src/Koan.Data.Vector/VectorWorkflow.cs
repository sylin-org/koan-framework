using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

public static class VectorWorkflow<TEntity>
    where TEntity : class, IEntity<string>
{
    public static bool IsAvailable(string? profileName = null)
        => ResolveRegistryOrNull()?.IsAvailable<TEntity>(profileName) ?? false;

    public static IVectorWorkflow<TEntity> For(string? profileName = null)
    {
        var registry = ResolveRegistry();
        if (!registry.IsEnabled)
        {
            throw new System.InvalidOperationException(
                "Vector workflows are disabled. Set Koan:Data:Vector:EnableWorkflows=true to enable this feature.");
        }

        return registry.GetWorkflow<TEntity>(profileName);
    }

    public static Task Save(
        TEntity entity,
        float[] embedding,
        object? metadata = null,
        string? profileName = null,
        CancellationToken ct = default)
        => For(profileName).Save(entity, embedding, metadata, ct);

    public static Task<VectorWorkflowSaveManyResult> SaveMany(
        IEnumerable<(TEntity Entity, float[] Embedding, object? Metadata)> items,
        string? profileName = null,
        CancellationToken ct = default)
        => For(profileName).SaveMany(items, ct);

    public static Task<bool> Delete(string id, string? profileName = null, CancellationToken ct = default)
        => For(profileName).Delete(id, ct);

    public static Task<int> DeleteMany(IEnumerable<string> ids, string? profileName = null, CancellationToken ct = default)
        => For(profileName).DeleteMany(ids, ct);

    public static Task EnsureCreated(string? profileName = null, CancellationToken ct = default)
        => For(profileName).EnsureCreated(ct);

    public static Task<VectorQueryResult<string>> Query(
        float[] vector,
        string? text = null,
        int? topK = null,
        double? alpha = null,
        object? filter = null,
        string? vectorName = null,
        string? profileName = null,
        CancellationToken ct = default)
        => For(profileName).Query(vector, text, topK, alpha, filter, vectorName, ct);

    public static Task<VectorQueryResult<string>> Query(
        VectorQueryOptions options,
        string? profileName = null,
        CancellationToken ct = default)
        => For(profileName).Query(options, ct);

    private static IVectorWorkflowRegistry ResolveRegistry()
        => ResolveRegistryOrNull()
           ?? throw new System.InvalidOperationException(
               "Vector workflows are unavailable. Ensure services.AddKoanDataVector() has been called during startup.");

    private static IVectorWorkflowRegistry? ResolveRegistryOrNull()
        => AppHost.Current?.GetService<IVectorWorkflowRegistry>();
}
