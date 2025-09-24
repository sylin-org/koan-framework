namespace Koan.Data.Relational.Orchestration;

public interface IRelationalStoreFeatures
{
    bool SupportsJsonFunctions { get; }
    bool SupportsPersistedComputedColumns { get; }
    bool SupportsIndexesOnComputedColumns { get; }

    /// <summary>
    /// Provider name for column type optimization (e.g., "postgresql", "sqlserver", "sqlite").
    /// </summary>
    string ProviderName { get; }
}