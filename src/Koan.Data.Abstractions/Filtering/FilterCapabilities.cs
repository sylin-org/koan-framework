namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Declares, per adapter, which <see cref="FilterOperator"/>s it can push down on scalar
/// vs. collection fields, plus whether it can push nested paths and case-insensitive
/// comparisons. This replaces the operator-blind <c>QueryCapabilities</c> flags as the thing
/// the <c>FilterPushdownCoordinator</c> negotiates against; the coarse flags remain a derived
/// summary. An adapter that cannot push a node declares it absent here, and the splitter
/// routes it to the in-memory residual instead of silently mistranslating.
/// </summary>
public sealed record FilterCapabilities(
    IReadOnlySet<FilterOperator> ScalarOperators,
    IReadOnlySet<FilterOperator> CollectionOperators,
    bool NestedPaths = true,
    bool IgnoreCase = false)
{
    /// <summary>Can this adapter push the operator on a field of the given kind?</summary>
    public bool CanPush(FilterOperator op, bool collectionField)
        => (collectionField ? CollectionOperators : ScalarOperators).Contains(op);

    private static readonly IReadOnlySet<FilterOperator> NoOps = new HashSet<FilterOperator>();
    private static readonly IReadOnlySet<FilterOperator> EveryOp =
        new HashSet<FilterOperator>((FilterOperator[])Enum.GetValues(typeof(FilterOperator)));

    /// <summary>Pushes nothing — every node becomes residual (the always-in-memory baseline).</summary>
    public static FilterCapabilities None { get; } = new(NoOps, NoOps, NestedPaths: false, IgnoreCase: false);

    /// <summary>Pushes everything (e.g. the in-memory/JSON/Redis adapters, which evaluate the AST directly).</summary>
    public static FilterCapabilities Full { get; } = new(EveryOp, EveryOp, NestedPaths: true, IgnoreCase: true);
}
