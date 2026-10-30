using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Relational.Linq;

namespace Koan.Data.Connector.SqlServer.Runtime;

internal static class SqlServerFeatures
{
    private static readonly Capability[] All =
    [
        DataCaps.Persistency,
        DataCaps.Query.Linq, DataCaps.Query.String, DataCaps.Query.FastCount,
        DataCaps.Query.ProviderBoundedPaging, DataCaps.Query.Filter, DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert, DataCaps.Write.BulkDelete, DataCaps.Write.AtomicBatch,
        DataCaps.Write.FastRemove, DataCaps.Write.ConditionalReplace,
        DataCaps.Isolation.RowScoped, DataCaps.Isolation.ContainerScoped, DataCaps.Isolation.DatabaseScoped
    ];

    public static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    // A scoped In past the parameters one statement binds is sliced by Koan (DATA-0162).
    private static readonly FilterSupport SetBoundFilterSupport =
        // A scoped slice leaves room in the statement for the scope's own parameters, so it stays a parameter list.
        RelationalFilterSupport.Default with { MaximumSetMembers = Infrastructure.Constants.MaximumParameters - 100 };

    public static void Describe(ICapabilities capabilities)
    {
        foreach (var capability in All) capabilities.Add(capability);
        capabilities
            .Add(DataCaps.Query.Filter, SetBoundFilterSupport)
            .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native));
    }
}
