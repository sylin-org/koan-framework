using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Abstraction over persistence operations required by the canon runtime.
/// </summary>
public interface ICanonPersistence
{
    /// <summary>
    /// Persists the canonical entity and returns the materialized snapshot.
    /// </summary>
    Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
        where TModel : CanonEntity<TModel>, new();

    /// <summary>
    /// Persists the staging record and returns the materialized snapshot.
    /// </summary>
    Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
        where TModel : CanonEntity<TModel>, new();

    /// <summary>
    /// Retrieves an aggregation index entry if one exists for the provided key.
    /// </summary>
    Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts an aggregation index entry.
    /// </summary>
    Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken);
}
