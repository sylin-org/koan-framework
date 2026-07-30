using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Complete vector adapter contract: vector-repository creation. Discovery, naming and source-routing come from
/// <see cref="IAdapterFactory"/> (ARCH-0103 §4.1 — the marker base shared with <see cref="IDataAdapterFactory"/>);
/// this adds vector repository creation from an immutable space plan.
/// </summary>
/// <remarks>
/// Vector Core resolves the routed source and compiles the immutable <see cref="VectorSpacePlan"/> before repository
/// creation. Plan-bound adapters realize its source, physical name, dimensions, metric, visibility, and model as one
/// decision. The source-only overload remains the compatibility floor for factories whose source is their complete
/// physical shape; adapters that need the complete space contract override the plan overload.
/// </remarks>
public interface IVectorAdapterFactory : IAdapterFactory
{
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => throw new NotSupportedException(
            $"Vector adapter '{Provider}' requires an immutable VectorSpacePlan. Declare the Entity space inside AddKoan(...).");

    /// <summary>Creates a repository from one immutable source-owned vector-space decision.</summary>
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, VectorSpacePlan plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Create<TEntity, TKey>(sp, plan.Source);
}
