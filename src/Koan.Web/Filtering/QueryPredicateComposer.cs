using System.Linq.Expressions;

namespace Koan.Web.Filtering;

/// <summary>
/// AND-composes the user's <c>?filter=</c> predicate with hook-contributed predicates from
/// <c>QueryOptions.Predicates</c>. Each contributing lambda owns its own parameter; the composer
/// rewrites every body to share a single new parameter so the resulting expression tree is
/// well-formed for adapter LINQ providers (EF, Mongo, SqlServer) to push down without
/// parameter-binding errors. See WEB-0068.
/// </summary>
internal static class QueryPredicateComposer
{
    /// <summary>
    /// Compose <paramref name="user"/> AND every entry of <paramref name="hooks"/> into a single
    /// <see cref="Expression{TDelegate}"/>.
    /// </summary>
    /// <returns>
    /// <c>null</c> when both inputs are empty (the caller falls through to free-text <c>Q</c>
    /// or fetch-all); the lone non-empty input when only one side contributes; the full
    /// AND-chain otherwise.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an entry in <paramref name="hooks"/> is not an
    /// <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c> — typically a sign that
    /// <c>QueryOptions.Predicates</c> was populated by something other than
    /// <c>QueryOptionsExtensions.AddPredicate&lt;TEntity&gt;</c>.
    /// </exception>
    public static Expression<Func<TEntity, bool>>? AndAll<TEntity>(
        Expression<Func<TEntity, bool>>? user,
        IReadOnlyList<LambdaExpression> hooks)
    {
        if (hooks is null || hooks.Count == 0) return user;

        Expression<Func<TEntity, bool>>? combined = user;
        foreach (var raw in hooks)
        {
            var typed = raw as Expression<Func<TEntity, bool>>
                ?? throw new InvalidOperationException(
                    $"QueryOptions.Predicates entry was {raw?.GetType().FullName ?? "null"}, expected " +
                    $"Expression<Func<{typeof(TEntity).FullName}, bool>>. Use " +
                    "QueryOptions.AddPredicate<TEntity>(...) so the lambda type is enforced at compile time.");

            combined = combined is null ? typed : And(combined, typed);
        }
        return combined;
    }

    private static Expression<Func<TEntity, bool>> And<TEntity>(
        Expression<Func<TEntity, bool>> left,
        Expression<Func<TEntity, bool>> right)
    {
        var param = Expression.Parameter(typeof(TEntity), "e");
        var leftBody = new ParameterRewriter(left.Parameters[0], param).Visit(left.Body)!;
        var rightBody = new ParameterRewriter(right.Parameters[0], param).Visit(right.Body)!;
        var body = Expression.AndAlso(leftBody, rightBody);
        return Expression.Lambda<Func<TEntity, bool>>(body, param);
    }

    private sealed class ParameterRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterRewriter(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _from ? _to : base.VisitParameter(node);
    }
}
