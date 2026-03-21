namespace Koan.AI.Training;

/// <summary>
/// Analysis results for a training dataset: sample counts, token distribution,
/// and estimated training time.
/// </summary>
public sealed record DatasetAnalysis
{
    /// <summary>Total number of samples in the dataset.</summary>
    public required int TotalSamples { get; init; }

    /// <summary>Average input token count per sample.</summary>
    public required int AvgInputTokens { get; init; }

    /// <summary>Average output token count per sample.</summary>
    public required int AvgOutputTokens { get; init; }

    /// <summary>Maximum input token count across all samples.</summary>
    public required int MaxInputTokens { get; init; }

    /// <summary>Maximum output token count across all samples.</summary>
    public required int MaxOutputTokens { get; init; }

    /// <summary>Estimated training time (formatted, e.g., "2h 30m").</summary>
    public string? EstimatedTrainTime { get; init; }
}
