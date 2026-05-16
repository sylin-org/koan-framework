using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Internal service interface behind the Training.* static facade.
/// Resolved via DI. Implementations handle job submission, monitoring, and lifecycle.
/// </summary>
public interface ITrainingService
{
    Task<TrainingJob> Train(TrainOptions options, IProgress<TrainingProgress>? progress = null, CancellationToken ct = default);
    Task<TrainingJob> Run(RunOptions options, IProgress<TrainingProgress>? progress = null, CancellationToken ct = default);
    Task<TrainingJob> Align(AlignOptions options, CancellationToken ct = default);
    Task<TrainingEstimate> Estimate(TrainOptions options, CancellationToken ct = default);
    Task<TrainingJob> Status(string jobId, CancellationToken ct = default);
    Task Cancel(string jobId, CancellationToken ct = default);
    Task<TrainingJob> Resume(string jobId, string? checkpoint = null, CancellationToken ct = default);
    Task<IReadOnlyList<TrainingJob>> List(CancellationToken ct = default);
}
