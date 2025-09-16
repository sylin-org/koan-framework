namespace Koan.Data.Relational.Orchestration;

public interface IRelationalStoreFeatures
{
    bool SupportsJsonFunctions { get; }
    bool SupportsPersistedComputedColumns { get; }
    bool SupportsIndexesOnComputedColumns { get; }
}