using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Snapshot of a training job's state.
/// </summary>
public sealed record TrainingJob
{
    /// <summary>Unique job identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Current job status.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>Base model being trained.</summary>
    public required ModelRef Base { get; init; }

    /// <summary>Training method used.</summary>
    public required string Method { get; init; }

    /// <summary>Output model reference (available after completion).</summary>
    public ModelRef? OutputModel { get; init; }

    /// <summary>When the job started.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>When the job completed (null if still running).</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Training metrics (available after completion).</summary>
    public TrainingMetrics? Metrics { get; init; }
}

/// <summary>
/// Final training metrics from a completed job.
/// </summary>
public sealed record TrainingMetrics(
    double FinalLoss,
    double? EvalLoss,
    int TotalSteps,
    int Epochs);
