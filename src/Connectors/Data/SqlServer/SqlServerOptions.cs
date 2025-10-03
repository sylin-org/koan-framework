using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.SqlServer;

public sealed class SqlServerOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    // Object materialization/serialization
    public bool JsonCaseInsensitive { get; set; } = true;
    public bool JsonWriteIndented { get; set; } = false;
    public bool JsonIgnoreNullValues { get; set; } = false;
    // Governance
    public SchemaDdlPolicy DdlPolicy { get; set; } = SchemaDdlPolicy.AutoCreate;
    public SchemaMatchingMode SchemaMatching { get; set; } = SchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
