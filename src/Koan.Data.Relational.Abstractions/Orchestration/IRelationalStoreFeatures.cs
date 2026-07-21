namespace Koan.Data.Relational.Orchestration;

/// <summary>Describes provider mechanics used by relational schema orchestration.</summary>
public interface IRelationalStoreFeatures
{
    bool SupportsJsonFunctions { get; }
    bool SupportsPersistedComputedColumns { get; }
    bool SupportsIndexesOnComputedColumns { get; }
    string ProviderName { get; }
}
