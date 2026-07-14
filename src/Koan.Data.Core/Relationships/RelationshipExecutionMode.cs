namespace Koan.Data.Core.Relationships;

/// <summary>Physical strategy selected for a relationship query.</summary>
public enum RelationshipExecutionMode
{
    Native,
    InMemory,
    BoundedScan,
    BoundedFallback
}
