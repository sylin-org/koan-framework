using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.CouchDb.Runtime;

/// <summary>
/// CouchDB's declared capabilities. Mango lowers every scalar operator and the collection operators
/// its selector language truly supports; bare equality against an array element and element-regex do
/// not match Mango semantics and stay undeclared. Sorts are index-gated on CouchDB — only the
/// identity index is free — so no sort lowering is claimed. Streams are not announced (no server-side
/// cursor in Mango): the fail-closed rejection is the tested behavior. Batches ride `_bulk_docs`,
/// which commits per document, so no atomicity is claimed.
/// </summary>
internal static class CouchDbFeatures
{
    internal static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Query.Linq,
        DataCaps.Query.String,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Write.ConditionalReplace,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    internal static readonly FilterSupport MangoFilterSupport = new(
        ScalarOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Eq, FilterOperator.Ne,
            FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
            FilterOperator.In, FilterOperator.Nin,
            FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
            FilterOperator.Exists
        },
        CollectionOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll,
            FilterOperator.HasNone, FilterOperator.Size
        },
        NestedPaths: true,
        IgnoreCase: false);

    internal static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    internal static void Describe(ICapabilities capabilities)
    {
        foreach (var capability in All) capabilities.Add(capability);
        capabilities
            .Add(DataCaps.Query.Filter, MangoFilterSupport)
            .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native));
    }
}
