using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Level 4 escape hatch: run an arbitrary training script with Koan compute orchestration.
/// </summary>
public sealed record RunOptions
{
    /// <summary>Path to the training script.</summary>
    public required string Script { get; init; }

    /// <summary>Optional base model reference.</summary>
    public ModelRef? Base { get; init; }

    /// <summary>Optional dataset reference.</summary>
    public DatasetRef? Data { get; init; }

    /// <summary>Compute requirements.</summary>
    public ComputeRequirement? Compute { get; init; }

    /// <summary>Container image to run the script in.</summary>
    public string? Image { get; init; }

    /// <summary>Additional pip/package dependencies.</summary>
    public string[]? Dependencies { get; init; }

    /// <summary>Maximum wall-clock time for the job.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Maximum memory in bytes.</summary>
    public long? MaxMemoryBytes { get; init; }
}
