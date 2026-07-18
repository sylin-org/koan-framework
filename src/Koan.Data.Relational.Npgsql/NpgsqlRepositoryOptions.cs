using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Relational.Npgsql;

public sealed class NpgsqlRepositoryOptions
{
    public required string ConnectionString { get; init; }
    public required string ProviderName { get; init; }
    public string? SearchPath { get; init; } = "public";
    public int DefaultPageSize { get; init; } = 50;
    public StorageNamingStyle NamingStyle { get; init; } = StorageNamingStyle.HashedNamespace;
    public string Separator { get; init; } = ".";
    public RelationalDdlPolicy DdlPolicy { get; init; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; init; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; init; }
    public string StableOrderClause { get; init; } = "ORDER BY ctid";
}
