using Koan.Core;

namespace Koan.Data.Relational.Orchestration;

public sealed class RelationalMaterializationOptions
{
    public RelationalMaterializationPolicy Materialization { get; set; } = RelationalMaterializationPolicy.None;
    public bool ProbeOnStartup { get; set; } = !KoanEnv.IsProduction;
    public bool FailOnMismatch { get; set; } = false; // escalated based on Materialization by configurator
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; } = false;
    public string? DefaultSchema { get; set; } = "dbo";
}