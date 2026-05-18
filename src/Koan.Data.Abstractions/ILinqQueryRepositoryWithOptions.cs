using System.Linq.Expressions;

namespace Koan.Data.Abstractions;

/// <summary>
/// The primary queryable interface for data adapters that support sort/page/filter pushdown.
/// </summary>
/// <remarks>
/// <para>
/// Every queryable adapter implements this. A <c>null</c> predicate means "no filter" — adapter
/// returns the whole entity set (subject to <paramref name="options"/> pagination/sort).
/// </para>
/// <para>
/// This interface replaced the untyped <c>IDataRepositoryWithOptions.Query(object?, options, ct)</c>
/// slot, which silently returned the full set on every adapter that didn't dispatch on
/// <c>Expression&lt;&gt;</c> in a runtime switch — six adapters had the same silent-degrade bug
/// before DATA-0095 Phase 1b made it a compile-time check.
/// </para>
/// </remarks>
public interface ILinqQueryRepositoryWithOptions<TEntity, TKey> : ILinqQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// LINQ-predicate query with sort/page/filter hints. Pass <c>null</c> for predicate when
    /// you want every entity in the set (e.g. paginated list with sort but no WHERE).
    /// </summary>
    Task<RepositoryQueryResult<TEntity>> Query(
        Expression<Func<TEntity, bool>>? predicate,
        DataQueryOptions? options,
        CancellationToken ct = default);
}
