using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions;

/// <summary>
/// The single repository query contract — an adapter is a <b>translator + executor</b>, never a
/// query orchestrator. Replaces the former <c>ILinqQueryRepository</c> /
/// <c>ILinqQueryRepositoryWithOptions</c> / <c>IStringQueryRepository(WithOptions)</c> family and
/// the untyped <c>object?</c> slot.
///
/// CONTRACT (DATA-XXXX): the framework's <c>FilterPushdownCoordinator</c> splits the caller's filter
/// against this adapter's declared filter support (the <c>FilterSupport</c> detail on the adapter's
/// <c>DataCaps.Query.Filter</c> capability token, ARCH-0084) and invokes <see cref="Query"/>
/// with a <see cref="QueryDefinition"/> whose <c>Filter</c> contains <b>only nodes this adapter
/// declared pushable</b>. The adapter therefore translates the WHOLE filter it receives — it never
/// computes a residual, never splits, never falls back. The coordinator evaluates the residual,
/// finishes unhandled sort, and paginates AFTER, centrally. When the coordinator has a residual it
/// strips pagination before calling the adapter (paginating a partially-filtered set is wrong).
///
/// The adapter reports, per axis, what it handled natively (sort/pagination/projection) plus
/// <c>TotalCount</c> when it can compute it cheaply; the coordinator trusts those flags.
/// </summary>
public interface IQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Execute a query whose filter is guaranteed pushable per the adapter's declared
    /// <c>FilterSupport</c> (the detail on its <c>DataCaps.Query.Filter</c> token); report per-axis what
    /// was handled natively.</summary>
    Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default);

    /// <summary>Count matching entities for a guaranteed-pushable filter + count strategy (no materialization).</summary>
    Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default);
}
