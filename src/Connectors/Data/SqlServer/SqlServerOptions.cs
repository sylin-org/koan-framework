using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.SqlServer;

public sealed class SqlServerOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto";
    public string Schema { get; set; } = Infrastructure.Constants.DefaultSchema;
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; }
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
