namespace Koan.Data.Abstractions.Failures;

/// <summary>What Data can safely say about native commit when an operation fails.</summary>
public enum DataCommitOutcome
{
    NotApplicable,
    NotDispatched,
    NotCommitted,
    Committed,
    Unknown
}
