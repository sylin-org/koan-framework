using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.Json.Runtime;

internal static class JsonFeatures
{
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

    internal static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    internal static void DescribeBackend(ICapabilities capabilities) => capabilities
        .Add(DataCaps.Write.BulkUpsert)
        .Add(DataCaps.Write.BulkDelete)
        .Add(
            DataCaps.Query.FilterExecution,
            new FilterExecutionProfile(FilterExecutionKind.Scan, SupportsBoundedCandidates: true));

    internal static void DescribeIndividualFilesBackend(ICapabilities capabilities) => capabilities
        .Add(
            DataCaps.Query.FilterExecution,
            new FilterExecutionProfile(FilterExecutionKind.Scan, SupportsBoundedCandidates: true));
}
