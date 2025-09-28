using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Sqlite;

public sealed class SqliteOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    // Paging guardrails (ADR-0044)
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    // Schema policy
    public SchemaDdlPolicy DdlPolicy { get; set; } = SchemaDdlPolicy.AutoCreate; // default per note
    public SchemaMatchingMode SchemaMatching { get; set; } = SchemaMatchingMode.Relaxed; // default per note
    // Global safety: allow DDL in prod only with an explicit magic flag
    public bool AllowProductionDdl { get; set; } = false;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}