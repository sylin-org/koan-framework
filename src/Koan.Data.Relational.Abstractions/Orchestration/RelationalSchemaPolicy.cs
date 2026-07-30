namespace Koan.Data.Relational.Orchestration;

using Koan.Data.Abstractions.Sources;

/// <summary>Immutable schema decisions for one provider/source route.</summary>
public sealed record RelationalSchemaPolicy
{
    public RelationalProjectionMode Projections { get; init; } = RelationalProjectionMode.None;
    public RelationalDdlPolicy Ddl { get; init; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode Matching { get; init; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; init; }
    public string DefaultSchema { get; init; } = "dbo";
    public StorageLifecycle StorageLifecycle { get; init; } = StorageLifecycle.Managed;
}
