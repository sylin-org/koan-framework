namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Declares, per vector adapter, which <see cref="FilterOperator"/>s it can push down onto stored
/// metadata, plus whether it can push nested paths and case-insensitive comparisons. The vector
/// analogue of <see cref="FilterCapabilities"/>, with one deliberate difference: there is
/// <b>no scalar-vs-collection split</b>. Vector metadata is schemaless — a key's value type is not
/// statically known — so the capability is a single operator set rather than two.
/// </summary>
/// <remarks>
/// AI-0036 §9 / DATA-0097 P1. This is the contract the <c>VectorFilterCoordinator</c> negotiates
/// against. Unlike the entity path, an operator the adapter cannot push is <b>not</b> routed to an
/// in-memory residual — vector search has no in-memory floor (post-kNN filtering silently
/// under-returns, DATA-0097 §3), so an un-pushable node is a hard error. An adapter therefore
/// declares an operator here <b>only if it can honour the locked null/Nin/HasNone semantics</b> for
/// it; an operator it can name but not honour faithfully must be left out so it fails loud instead
/// of silently mis-returning.
/// <para>
/// It lives in <c>Koan.Data.Abstractions</c> (not <c>Koan.Data.Vector</c>) because it is a pure
/// filter-model value object over <see cref="FilterOperator"/> and is consumed by
/// <see cref="FilterSplitter"/> in this same assembly; the vector connectors (which reference this
/// assembly) declare instances of it.
/// </para>
/// </remarks>
public sealed record VectorFilterCapabilities(
    IReadOnlySet<FilterOperator> Operators,
    bool NestedPaths = true,
    bool IgnoreCase = false)
{
    /// <summary>Can this adapter push the operator onto a metadata field?</summary>
    public bool CanPush(FilterOperator op) => Operators.Contains(op);

    private static readonly IReadOnlySet<FilterOperator> NoOps = new HashSet<FilterOperator>();
    private static readonly IReadOnlySet<FilterOperator> EveryOp =
        new HashSet<FilterOperator>((FilterOperator[])Enum.GetValues(typeof(FilterOperator)));

    /// <summary>Pushes nothing — any non-empty filter becomes a residual and therefore a hard error.</summary>
    public static VectorFilterCapabilities None { get; } = new(NoOps, NestedPaths: false, IgnoreCase: false);

    /// <summary>Pushes every operator (e.g. the in-memory test adapter / oracle).</summary>
    public static VectorFilterCapabilities Full { get; } = new(EveryOp, NestedPaths: true, IgnoreCase: true);

    /// <summary>Builds a capability set from an explicit operator list (the per-adapter declaration form).</summary>
    public static VectorFilterCapabilities Of(bool nestedPaths, bool ignoreCase, params FilterOperator[] operators)
        => new(new HashSet<FilterOperator>(operators), nestedPaths, ignoreCase);
}
