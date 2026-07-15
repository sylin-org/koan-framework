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
/// computes a residual, never splits, never falls back. For a materialized result, the coordinator
/// evaluates the residual, finishes unhandled sort, and paginates afterward; it strips provider
/// pagination when a residual exists because paginating a partially filtered final result is wrong.
/// For DATA-0107 provider-bounded streaming, the coordinator instead pages the pushable candidate set,
/// evaluates the residual pointwise, and continues across empty output pages. That distinct mode
/// requires provider-handled pagination and total ordering before it yields any candidate.
///
/// The adapter reports, per axis, what it handled natively (sort/pagination/projection) plus
/// <c>TotalCount</c> when the query explicitly requests a <see cref="CountStrategy"/>. A null count
/// strategy means no total was requested; adapters must not add count work merely because a page was
/// requested. The coordinator trusts the execution flags.
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
