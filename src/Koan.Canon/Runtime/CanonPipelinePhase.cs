namespace Koan.Canon;

/// <summary>
/// Discrete phases of the canonization pipeline.
/// </summary>
public enum CanonPipelinePhase
{
    Intake = 0,
    Validation = 1,
    Matching = 2,
    Reconcile = 3,
    Projection = 4,
    Distribution = 5
}
