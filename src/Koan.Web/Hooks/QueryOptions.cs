using System.Linq.Expressions;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Web.Hooks;

/// <summary>
/// Query and shaping options flowing through the controller and hooks.
/// </summary>
public sealed class QueryOptions
{
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = Infrastructure.KoanWebConstants.Defaults.DefaultPageSize;

    /// <summary>
    /// Structured sort specs with resolved MemberPath. Built by EntityQueryParser (strict-by-default)
    /// and mutated by hooks. See DATA-0092.
    /// </summary>
    public List<SortSpec> Sort { get; set; } = new();

    public string Shape { get; set; } = "full"; // full | map | dict
    public string? View { get; set; }
    public Dictionary<string, string> Extras { get; } = new();

    /// <summary>
    /// Additive server-side predicates contributed by <see cref="IRequestOptionsHook{TEntity}"/>
    /// implementations. AND-composed with the user's <c>?filter=</c> at query-execution time so the
    /// adapter counts and pages against the already-filtered set — see WEB-0068.
    /// <para>
    /// Each entry is an <see cref="Expression{TDelegate}"/> typed
    /// <c>Func&lt;TEntity, bool&gt;</c> for the request's entity. Add via
    /// <see cref="QueryOptionsExtensions.AddPredicate{TEntity}"/> so the lambda type is enforced
    /// at compile time.
    /// </para>
    /// <para>
    /// When this list is non-empty, <see cref="Q"/> is dropped — the free-text and predicate
    /// paths reach different repository surfaces and can't be composed at the framework layer.
    /// </para>
    /// </summary>
    public List<LambdaExpression> Predicates { get; } = new();
}
