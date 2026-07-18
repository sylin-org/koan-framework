using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Postgres;

public sealed class PostgresOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    // PostgreSQL truncates identifiers at 63 bytes, so FullNamespace overflows for non-trivial namespaces
    // (and collapses partitions on truncation). HashedNamespace keeps names short + collision-safe.
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.HashedNamespace;
    public string Separator { get; set; } = ".";
    public string? SearchPath { get; set; } = "public";
    public int DefaultPageSize { get; set; } = 50;
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
