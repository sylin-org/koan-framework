using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.InMemory.Runtime;

internal static class InMemoryFeatures
{
    private static readonly IReadOnlyList<Capability> Backend =
    [
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete
    ];

    private static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Query.Linq,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    public static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    public static void DescribeBackend(ICapabilities capabilities)
    {
        foreach (var capability in Backend) capabilities.Add(capability);
        capabilities.Add(
            DataCaps.Query.FilterExecution,
            new FilterExecutionProfile(FilterExecutionKind.InMemory, SupportsBoundedCandidates: true));
    }
}
