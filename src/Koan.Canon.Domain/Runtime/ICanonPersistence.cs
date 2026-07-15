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
    /// Retrieves a canonical entity by identifier, or <see langword="null"/> when it does not exist.
    /// </summary>
    /// <remarks>
    /// Custom persistence implementations own this read together with canonical writes. Returning
    /// <see langword="null"/> means the record is absent; storage and provider failures must propagate.
    /// </remarks>
    Task<TModel?> GetCanonicalAsync<TModel>(string canonicalId, CancellationToken cancellationToken)
        where TModel : CanonEntity<TModel>, new();

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
    Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts an aggregation index entry.
    /// </summary>
    Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken);
}
