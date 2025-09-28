using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Postgres;

public sealed class PostgresOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public string? SearchPath { get; set; } = "public";
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    public SchemaDdlPolicy DdlPolicy { get; set; } = SchemaDdlPolicy.AutoCreate;
    public SchemaMatchingMode SchemaMatching { get; set; } = SchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}