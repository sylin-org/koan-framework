using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Firebird;

public sealed class FirebirdOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto";
    public string Database { get; set; } = Infrastructure.Constants.DefaultDatabase;
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.HashedNamespace;
    public string Separator { get; set; } = ".";
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; }
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
