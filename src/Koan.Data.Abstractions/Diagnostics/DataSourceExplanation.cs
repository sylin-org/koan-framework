namespace Koan.Data.Abstractions;

/// <summary>Pure explanation of one named operation against the same decision used by execution.</summary>
public sealed record DataSourceExplanation(
    string SourceDecisionId,
    string Provider,
    DataOperationDescription Operation,
    IReadOnlyList<string> ClaimReferences);
