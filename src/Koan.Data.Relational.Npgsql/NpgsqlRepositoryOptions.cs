using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Relational.Npgsql;

public sealed class NpgsqlRepositoryOptions
{
    public required string ConnectionString { get; init; }
    public required string ProviderName { get; init; }
    public string Source { get; init; } = "Default";
    public string SearchPath { get; init; } = "public";
    public StorageNamingStyle NamingStyle { get; init; } = StorageNamingStyle.HashedNamespace;
    public string Separator { get; init; } = ".";
    public RelationalDdlPolicy DdlPolicy { get; init; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; init; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; init; }
    public DataSourcePlan SourcePlan { get; init; } = DataSourcePlan.Default;

    /// <summary>
    /// Whether index names are unique per schema and truncated to 63 bytes, as on PostgreSQL. When true, every index
    /// the schema owner creates is confirmed in the catalog, and a declared index that a name collision would silently
    /// skip is refused (NK-056). CockroachDB scopes index names to their table and sets false.
    /// </summary>
    public bool IndexNamesAreSchemaWide { get; init; } = true;

    /// <summary>
    /// Whether this store keeps a lexical index: a stored generated <c>tsvector</c> column with a GIN index, which
    /// PostgreSQL holds and CockroachDB does not in the form this runtime spells. The factory that sets it also
    /// declares <c>query.lexical</c> in its claims.
    /// </summary>
    public bool SupportsLexicalIndex { get; init; }
}
