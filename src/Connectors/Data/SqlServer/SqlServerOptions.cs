using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.SqlServer;

public sealed class SqlServerOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    // Object materialization/serialization
    public bool JsonCaseInsensitive { get; set; } = true;
    public bool JsonWriteIndented { get; set; } = false;
    public bool JsonIgnoreNullValues { get; set; } = false;
    // Governance
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
