namespace Koan.Data.Abstractions;

/// <summary>Redacted summary of one declared aggregate-to-record decision.</summary>
public sealed record DataMappingDescription(
    string DecisionId,
    int IdentityParts,
    int ScalarBindings,
    int ObjectBindings,
    bool HasNestedPhysicalPaths);
