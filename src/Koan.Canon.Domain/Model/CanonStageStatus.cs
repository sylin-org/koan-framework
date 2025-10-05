namespace Koan.Canon.Domain.Model;

/// <summary>
/// Represents the processing state of a staged canonization payload.
/// </summary>
public enum CanonStageStatus
{
    /// <summary>
    /// Stage record has been created but not yet processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Stage record is currently being processed by the canon engine.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Stage record completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Stage record failed and requires intervention.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Stage record has been parked for manual triage.
    /// </summary>
    Parked = 4
}
