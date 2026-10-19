using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Web.Hooks;

/// <summary>
/// Query and shaping options flowing through the controller and hooks.
/// </summary>
public sealed class QueryOptions
{
    /// <summary>The normalized relationship expansion choice shared by endpoint and emit hooks.</summary>
    public bool IncludeRelationships { get; set; }

    /// <summary>
    /// The request's <c>q</c> text, untrusted. The framework never runs it as a provider query unless the controller
    /// declared <c>AllowRawQuery</c>; a <c>Q</c> still set when the read executes answers <c>400</c>. A request-options
    /// hook gives it a meaning by adding a predicate (<see cref="QueryOptionsExtensions.AddPredicate{TEntity}"/>) and
    /// setting <c>Q</c> to null (WEB-0068, WEB-0074).
    /// </summary>
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
    /// Complete normalized server constraint, AND-composed with the user's filter before execution.
    /// AddPredicate lowers recognized captured values when contributed. Null means unconstrained.
    /// A provider-native <see cref="Q"/> (a controller that declared <c>AllowRawQuery</c>) cannot compose with a
    /// filter, so the pair answers <c>400</c> instead of one silently winning.
    /// </summary>
    public Filter? Filter { get; set; }
}
