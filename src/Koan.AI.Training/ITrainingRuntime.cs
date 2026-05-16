using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// A runtime that can execute training jobs (Python sidecar, container, remote GPU).
/// Discovered via Reference = Intent — add the runtime package, get the capability.
/// </summary>
public interface ITrainingRuntime
{
    /// <summary>Runtime identifier (e.g., "python-local", "container-cuda").</summary>
    string Id { get; }

    /// <summary>Training methods this runtime supports.</summary>
    TrainMethod[] SupportedMethods { get; }

    /// <summary>Check if this runtime is available.</summary>
    Task<bool> IsAvailable(CancellationToken ct = default);

    /// <summary>Launch a training job.</summary>
    Task<TrainingJob> Launch(
        TrainOptions options,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Launch a custom script job.</summary>
    Task<TrainingJob> LaunchScript(
        RunOptions options,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Get job status.</summary>
    Task<TrainingJob> Status(string jobId, CancellationToken ct = default);

    /// <summary>Cancel a running job.</summary>
    Task Cancel(string jobId, CancellationToken ct = default);

    /// <summary>Resume from checkpoint.</summary>
    Task<TrainingJob> Resume(
        string jobId, string? checkpoint = null, CancellationToken ct = default);
}
