namespace Koan.AI.Training;

/// <summary>
/// Pre-flight estimate for a training job: cost, time, and compute recommendations.
/// </summary>
public sealed record TrainingEstimate
{
    /// <summary>Total token count in the dataset.</summary>
    public required long Tokens { get; init; }

    /// <summary>Estimated GPU hours for training.</summary>
    public required double EstimatedGpuHours { get; init; }

    /// <summary>Estimated cost (formatted, e.g., "$12.50").</summary>
    public string? EstimatedCost { get; init; }

    /// <summary>Recommended compute configuration description.</summary>
    public required string RecommendedCompute { get; init; }

    /// <summary>Whether the job can fit on the local GPU.</summary>
    public required bool FitsLocalGpu { get; init; }

    /// <summary>Explanation or caveat for the estimate.</summary>
    public string? Reason { get; init; }
}
