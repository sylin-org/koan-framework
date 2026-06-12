namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// The closed operator vocabulary shared by the DSL, the LINQ lifter, every
/// <c>IFilterTranslator</c>, and <c>InMemoryFilterEvaluator</c>. A DSL keyword maps to a
/// scalar or collection operator based on the resolved leaf type — e.g. <c>$in</c> on a
/// scalar field becomes <see cref="In"/>, while <c>$in</c> on a <c>List&lt;T&gt;</c> field
/// becomes <see cref="HasAny"/>. Redundant operators are intentionally absent: <c>$between</c>
/// lowers to <see cref="Gte"/> + <see cref="Lte"/>, and wildcard strings to
/// <see cref="StartsWith"/> / <see cref="EndsWith"/> / <see cref="Contains"/>.
/// </summary>
public enum FilterOperator
{
    // Scalar comparison
    Eq, Ne, Gt, Gte, Lt, Lte,

    // Scalar set membership (the field value is in / not in a set)
    In, Nin,

    // String pattern (single-argument, broadly provider-translatable)
    StartsWith, EndsWith, Contains,

    // Element presence
    Exists,

    // Collection membership (field is List<T> / array)
    Has,      // collection contains a single value
    HasAny,   // collection overlaps the set ($in on a collection)
    HasAll,   // collection is a superset of the set ($all)
    HasNone,  // collection is disjoint from the set ($nin on a collection)
    Size,     // collection element count equals the value
}
