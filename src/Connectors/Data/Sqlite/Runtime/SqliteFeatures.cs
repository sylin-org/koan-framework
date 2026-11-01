using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Relational.Linq;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal static class SqliteFeatures
{
    internal static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Persistency,
        DataCaps.Query.Linq,
        DataCaps.Query.String,
        DataCaps.Query.ProviderBoundedPaging,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Query.Lexical,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Write.AtomicBatch,
        DataCaps.Write.AtomicCommit,
        DataCaps.Write.HeldTransaction,
        DataCaps.Write.MutationOutcomes,
        DataCaps.Write.FastRemove,
        DataCaps.Write.ConditionalReplace,
        DataCaps.Write.ConditionalDelete,
        DataCaps.Write.InsertOnly,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    internal static void Declare(IDataClaims claims)
    {
        foreach (var capability in All) claims.Capability(capability);
    }

    // A scoped In past the parameters one statement binds is sliced by Koan (DATA-0162).
    private static readonly FilterSupport SetBoundFilterSupport =
        RelationalFilterSupport.Default with { MaximumSetMembers = Infrastructure.Constants.MaximumParameters };

    internal static void Describe(ICapabilities capabilities)
    {
        foreach (var capability in All) capabilities.Add(capability);
        capabilities
            .Add(DataCaps.Query.Filter, SetBoundFilterSupport)
            .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native));
    }
}
