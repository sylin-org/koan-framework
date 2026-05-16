namespace Koan.AI.Training;

/// <summary>
/// Real-time progress update from a training job.
/// </summary>
public sealed record TrainingProgress
{
    /// <summary>Current training step.</summary>
    public required int Step { get; init; }

    /// <summary>Total steps in the training run.</summary>
    public required int Total { get; init; }

    /// <summary>Current loss value.</summary>
    public required double Loss { get; init; }

    /// <summary>Current learning rate.</summary>
    public required double LearningRate { get; init; }

    /// <summary>Current fractional epoch.</summary>
    public required double Epoch { get; init; }

    /// <summary>GPU memory usage in GiB (null if unavailable).</summary>
    public double? GpuMemoryGb { get; init; }

    /// <summary>Time elapsed since training started.</summary>
    public required TimeSpan ElapsedTime { get; init; }

    /// <summary>Estimated time remaining (null if unknown).</summary>
    public TimeSpan? EstimatedRemaining { get; init; }
}
