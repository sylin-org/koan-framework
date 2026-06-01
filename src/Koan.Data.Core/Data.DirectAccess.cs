using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>
/// Direct data-access conveniences that bypass the entity-centric <c>Entity&lt;T&gt;</c> base for
/// advanced/generic scenarios. Thin wrappers over <see cref="Data{TEntity,TKey}"/>, so they ride the
/// same unified <see cref="QueryDefinition"/> + <see cref="IQueryRepository{TEntity,TKey}"/> +
/// coordinator path (split → adapter → residual/sort/paginate-after) — no separate query logic.
/// </summary>
public static class DataDirectAccess
{
    /// <summary>Execute a LINQ predicate directly against the repository.</summary>
    public static Task<IReadOnlyList<TEntity>> Query<TEntity, TKey>(
        Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.Query(predicate, ct);

    /// <summary>Get all entities of a type directly.</summary>
    public static Task<IReadOnlyList<TEntity>> All<TEntity, TKey>(CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.All(ct);

    /// <summary>Count entities matching a predicate.</summary>
    public static Task<long> Count<TEntity, TKey>(
        Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Data<TEntity, TKey>.Count(predicate, CountStrategy.Exact, ct);
}
