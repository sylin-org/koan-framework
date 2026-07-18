using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Sqlite;

public sealed class SqliteOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    // Schema policy
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    // Global safety: allow DDL in prod only with an explicit magic flag
    public bool AllowProductionDdl { get; set; } = false;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
