using Sora.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Sora.Data.SqlServer;

public sealed class SqlServerOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Server=localhost;Database=sora;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
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
}