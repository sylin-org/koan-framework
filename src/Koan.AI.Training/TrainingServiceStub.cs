using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Placeholder implementation of <see cref="ITrainingService"/>.
/// All operations throw <see cref="NotImplementedException"/> until a training
/// runtime (e.g., Koan.AI.Training.Container or Koan.AI.Training.Python) is registered.
/// </summary>
internal sealed class TrainingServiceStub : ITrainingService
{
    private const string Message =
        "Training execution requires Koan.AI.Training.Container or " +
        "Koan.AI.Training.Python runtime package.";

    public Task<TrainingJob> TrainAsync(TrainOptions options, IProgress<TrainingProgress>? progress, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<TrainingJob> RunAsync(RunOptions options, IProgress<TrainingProgress>? progress, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<TrainingJob> AlignAsync(AlignOptions options, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<TrainingEstimate> EstimateAsync(TrainOptions options, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<TrainingJob> StatusAsync(string jobId, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task CancelAsync(string jobId, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<TrainingJob> ResumeAsync(string jobId, string? checkpoint, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<IReadOnlyList<TrainingJob>> ListAsync(CancellationToken ct)
        => throw new NotImplementedException(Message);
}
