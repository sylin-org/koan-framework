namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Discrete phases of the canonization pipeline.
/// </summary>
public enum CanonPipelinePhase
{
    Intake = 0,
    Validation = 1,
    Aggregation = 2,
    Policy = 3,
    Projection = 4,
    Distribution = 5
}
