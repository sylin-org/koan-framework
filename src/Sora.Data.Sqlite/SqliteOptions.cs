using Sora.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Sora.Data.Sqlite;

public sealed class SqliteOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Data Source=./data/app.db";
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
}