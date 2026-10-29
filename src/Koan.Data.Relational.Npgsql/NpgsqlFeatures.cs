using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Relational.Linq;

namespace Koan.Data.Relational.Npgsql;

public static class NpgsqlFeatures
{
    private static readonly IReadOnlyList<Capability> All =
    [
        DataCaps.Persistency,
        DataCaps.Query.Linq,
        DataCaps.Query.String,
        DataCaps.Query.ProviderBoundedPaging,
        DataCaps.Query.Filter,
        DataCaps.Query.FilterExecution,
        DataCaps.Write.BulkUpsert,
        DataCaps.Write.BulkDelete,
        DataCaps.Write.AtomicBatch,
        DataCaps.Write.FastRemove,
        DataCaps.Write.ConditionalReplace,
        DataCaps.Write.ConditionalDelete,
        DataCaps.Write.InsertOnly,
        DataCaps.Isolation.RowScoped,
        DataCaps.Isolation.ContainerScoped,
        DataCaps.Isolation.DatabaseScoped
    ];

    /// <summary>Declares the runtime's capabilities as a provider's claims.</summary>
    /// <param name="claims">The claim set the provider's factory describes itself into.</param>
    /// <param name="lexical">Whether the provider keeps a lexical index (<see cref="DataCaps.Query.Lexical"/>); PostgreSQL does, CockroachDB does not.</param>
    public static void Declare(IDataClaims claims, bool lexical = false)
    {
        foreach (var capability in All) claims.Capability(capability);
        if (lexical) claims.Capability(DataCaps.Query.Lexical);
    }

    /// <summary>Describes a repository's capabilities, with the filter and execution details every relational store carries.</summary>
    /// <param name="capabilities">The capability set the repository describes itself into.</param>
    /// <param name="lexical">Whether this route keeps a lexical index (<see cref="DataCaps.Query.Lexical"/>).</param>
    public static void Describe(ICapabilities capabilities, bool lexical = false)
    {
        foreach (var capability in All) capabilities.Add(capability);
        if (lexical) capabilities.Add(DataCaps.Query.Lexical);
        capabilities
            .Add(DataCaps.Query.Filter, RelationalFilterSupport.Default)
            .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native));
    }
}
