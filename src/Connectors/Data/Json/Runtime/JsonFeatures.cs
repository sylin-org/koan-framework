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
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    public static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    public static void DescribeBackend(ICapabilities capabilities) =>
        capabilities.Add(
            DataCaps.Query.FilterExecution,
            new FilterExecutionProfile(FilterExecutionKind.Scan, SupportsBoundedCandidates: true));
}
