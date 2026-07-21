using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

/// <summary>
/// Pipeline-stage transformer: same shape in, same shape out. Multiple enrichers can be registered
/// per entity type; the result filter applies every activated enricher in priority order, then
/// passes the enriched value to the Terminal stage (an <see cref="IEntityTransformer{TEntity, TShape}"/>
/// resolved via Accept negotiation) or the default JSON serializer.
/// </summary>
/// <remarks>
/// Implementations typically also implement <see cref="ITransformerActivationPredicate"/> to
/// declare when they fire — "only when the user is authenticated", "only when the request has
/// the X-Preview header", and so on. An enricher without a predicate runs whenever its controller
/// is opted in.
/// </remarks>
/// <typeparam name="TEntity">The entity (or projection) type carried on the response.</typeparam>
public interface IEntityEnricher<TEntity>
{
    /// <summary>
    /// Enrich a single entity. Return a new instance — implementations should treat <paramref name="model"/>
    /// as immutable.
    /// </summary>
    Task<TEntity> Enrich(TEntity model, HttpContext context);

    /// <summary>
    /// Enrich a collection. Implementations should batch any cross-cutting lookups (one query per
    /// request, not per item) to keep the pipeline cheap.
    /// </summary>
    Task<IReadOnlyList<TEntity>> EnrichMany(IReadOnlyList<TEntity> models, HttpContext context);
}
