using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal static class CouchbaseFeatures
{
    internal static readonly FilterSupport Filters = FilterSupport.Of(
        [
            FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Gt, FilterOperator.Gte,
            FilterOperator.Lt, FilterOperator.Lte, FilterOperator.In, FilterOperator.Nin,
            FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
            FilterOperator.Exists
        ],
        [
            FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll,
            FilterOperator.HasNone, FilterOperator.Size, FilterOperator.HasContains, FilterOperator.Exists
        ],
        nestedPaths: true,
        ignoreCase: false);

    internal static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Query.Linq,
        DataCaps.Query.ProviderBoundedPaging,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Write.ConditionalReplace,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    internal static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    internal static void Describe(ICapabilities capabilities)
    {
        foreach (var capability in All) capabilities.Add(capability);
        capabilities
            .Add(DataCaps.Query.Filter, Filters)
            .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native));
    }
}
