using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.Redis.Runtime;

internal static class RedisFeatures
{
    internal static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    internal static void Describe(ICapabilities capabilities, bool queryable)
    {
        foreach (var capability in All.Where(capability => queryable || !QueryCapabilities.Contains(capability)))
            capabilities.Add(capability);
        if (queryable)
            capabilities
                .Add(DataCaps.Query.Filter, FilterSupport.Full)
                .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Scan, true));
    }

    private static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Query.Linq,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Write.ConditionalReplace,
        DataCaps.Write.FastRemove,
        DataCaps.Retention.TtlIndex,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    private static readonly HashSet<Capability> QueryCapabilities =
    [
        DataCaps.Query.Linq,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution
    ];
}
