using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Relational.Linq;

/// <summary>
/// The <see cref="FilterCapabilities"/> the relational adapters (Sqlite / Postgres / SqlServer) can
/// push down via <see cref="SqlFilterTranslator"/>. Centralized so the relational trio declares one
/// honest, identical surface (DATA-XXXX).
///
/// Scalar operators map to native SQL (comparison, LIKE, IN/NOT IN, IS [NOT] NULL). Collection
/// operators map to native JSON-array containment (List&lt;string&gt; stored as a JSON array): Has /
/// HasAny / HasAll / HasNone via <c>JsonArrayContains</c>, Size via <c>JsonArrayLength</c>.
///
/// Deliberately NOT declared (so the coordinator routes them to the in-memory floor):
/// <list type="bullet">
/// <item><c>NestedPaths = false</c> — the adapters' column resolver maps flat property names only.</item>
/// <item><c>IgnoreCase = false</c> — case-insensitive string comparison is collation-dependent; the
/// floor owns it to stay convergent with the oracle.</item>
/// </list>
/// </summary>
public static class RelationalFilterCapabilities
{
    public static FilterCapabilities Default { get; } = new(
        ScalarOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Eq, FilterOperator.Ne,
            FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
            FilterOperator.In, FilterOperator.Nin,
            FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
            FilterOperator.Exists,
        },
        CollectionOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll,
            FilterOperator.HasNone, FilterOperator.Size,
        },
        NestedPaths: false,
        IgnoreCase: false);
}
