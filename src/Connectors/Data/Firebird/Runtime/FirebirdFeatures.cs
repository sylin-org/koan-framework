using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.Firebird.Runtime;

/// <summary>
/// Firebird's declared capabilities. Streaming is not announced: the adapter materializes bounded
/// pages and the fail-closed rejection for streams is the tested behavior (the AODB suite proves it).
/// Collection filter operators are deliberately absent — Firebird has no JSON functions — so the
/// coordinator routes them to the in-memory floor instead of asking the store to lie.
/// </summary>
internal static class FirebirdFeatures
{
    internal static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Query.Linq,
        DataCaps.Query.String,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Write.AtomicBatch,
        DataCaps.Write.FastRemove,
        DataCaps.Write.ConditionalReplace,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    private static readonly FilterSupport ScalarOnlyFilterSupport = new(
        ScalarOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Eq, FilterOperator.Ne,
            FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
            FilterOperator.In, FilterOperator.Nin,
            FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
            FilterOperator.Exists
        },
        CollectionOperators: new HashSet<FilterOperator>(),
        NestedPaths: false,
        IgnoreCase: false);

    internal static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    internal static void Describe(ICapabilities capabilities)
    {
        foreach (var capability in All) capabilities.Add(capability);
        capabilities
            .Add(DataCaps.Query.Filter, ScalarOnlyFilterSupport)
            .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native));
    }
}
