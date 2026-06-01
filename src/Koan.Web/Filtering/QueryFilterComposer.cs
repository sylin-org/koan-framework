using System.Linq.Expressions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Web.Filtering;

/// <summary>
/// Composes the user-supplied JSON filter with hook-contributed LINQ predicates (WEB-0068) into a
/// single normalized <see cref="Filter"/> using AND semantics. Hook predicates are lowered to the
/// unified AST via <see cref="LinqFilterCompiler"/> so everything converges on one representation
/// before reaching the orchestrator — no parameter-rebinding gymnastics, no Expression/AST split.
/// </summary>
internal static class QueryFilterComposer
{
    public static Filter? AndAll<TEntity>(
        Filter? userFilter,
        IReadOnlyList<Expression<Func<TEntity, bool>>> hookPredicates)
    {
        var all = new List<Filter>();
        if (userFilter is not null) all.Add(userFilter);
        if (hookPredicates is { Count: > 0 })
            foreach (var p in hookPredicates)
                all.Add(LinqFilterCompiler.Compile(p));

        return all.Count switch
        {
            0 => null,
            1 => all[0],
            _ => new AllOf(all)
        };
    }
}
