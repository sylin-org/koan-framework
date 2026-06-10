using System.Linq.Expressions;

namespace Koan.Jobs;

/// <summary>
/// Builds the pushed-down predicates the durable ledger hands the data layer (JOBS-0005 §19.3). Conditional
/// composition (only the non-null <see cref="JobQuery"/> axes become clauses) keeps the store-side filter tight —
/// no <c>field == null || ...</c> noise the adapter can't optimize.
/// </summary>
internal static class JobLedgerPredicates
{
    /// <summary>The declarative dashboard/facade query → a single conjunctive predicate (omitting wildcard axes).</summary>
    public static Expression<Func<JobRecord, bool>> ForQuery(JobQuery q)
    {
        Expression<Func<JobRecord, bool>>? p = null;
        if (q.WorkType is { } wt) p = And(p, r => r.WorkType == wt);
        if (q.WorkId is { } wid) p = And(p, r => r.WorkId == wid);
        if (q.Action is { } a) p = And(p, r => r.Action == a);
        if (q.Status is { } s) p = And(p, r => r.Status == s);
        return p ?? (static r => true);
    }

    /// <summary>Non-terminal = the lifecycle statuses below <see cref="JobStatus.Completed"/> (terminals are 4..7),
    /// so it pushes as one comparison rather than a four-way OR.</summary>
    public static Expression<Func<JobRecord, bool>> NonTerminal()
        => static r => r.Status < JobStatus.Completed;

    /// <summary>Terminal = <see cref="JobStatus.Completed"/> and above (4..7) — one comparison, the mirror of
    /// <see cref="NonTerminal"/>.</summary>
    public static Expression<Func<JobRecord, bool>> Terminal()
        => static r => r.Status >= JobStatus.Completed;

    private static Expression<Func<JobRecord, bool>> And(
        Expression<Func<JobRecord, bool>>? left, Expression<Func<JobRecord, bool>> right)
    {
        if (left is null) return right;
        var p = Expression.Parameter(typeof(JobRecord), "r");
        var body = Expression.AndAlso(
            new Rebind(left.Parameters[0], p).Visit(left.Body)!,
            new Rebind(right.Parameters[0], p).Visit(right.Body)!);
        return Expression.Lambda<Func<JobRecord, bool>>(body, p);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
