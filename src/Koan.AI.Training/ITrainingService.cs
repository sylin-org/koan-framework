using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Internal service interface behind the Training.* static facade.
/// Resolved via DI. Implementations handle job submission, monitoring, and lifecycle.
/// </summary>
public interface ITrainingService
{
    Task<TrainingJob> TrainAsync(TrainOptions options, IProgress<TrainingProgress>? progress = null, CancellationToken ct = default);
    Task<TrainingJob> RunAsync(RunOptions options, IProgress<TrainingProgress>? progress = null, CancellationToken ct = default);
    Task<TrainingJob> AlignAsync(AlignOptions options, CancellationToken ct = default);
    Task<TrainingEstimate> EstimateAsync(TrainOptions options, CancellationToken ct = default);
    Task<TrainingJob> StatusAsync(string jobId, CancellationToken ct = default);
    Task CancelAsync(string jobId, CancellationToken ct = default);
    Task<TrainingJob> ResumeAsync(string jobId, string? checkpoint = null, CancellationToken ct = default);
    Task<IReadOnlyList<TrainingJob>> ListAsync(CancellationToken ct = default);
}
