namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Indicates the result of a canonization attempt.
/// </summary>
public enum CanonizationOutcome
{
    /// <summary>
    /// Canonization produced a new or updated canonical record.
    /// </summary>
    Canonized = 0,

    /// <summary>
    /// Canonization skipped processing due to unchanged inputs.
    /// </summary>
    NoOp = 1,

    /// <summary>
    /// Canonization parked the record for manual intervention.
    /// </summary>
    Parked = 2,

    /// <summary>
    /// Canonization failed with an unrecoverable error.
    /// </summary>
    Failed = 3
}
