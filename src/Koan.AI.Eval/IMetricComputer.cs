using Koan.AI.Contracts.Shared;

namespace Koan.AI.Eval;

/// <summary>
/// Computes a single metric score for a model against a dataset.
/// Implement this interface to provide metric computation backends
/// (e.g., ROUGE, BLEU, F1 via Python, or custom .NET-based metrics).
/// </summary>
public interface IMetricComputer
{
    /// <summary>Compute a metric score for the given model and dataset.</summary>
    Task<double> ComputeAsync(ModelRef model, DatasetRef data, string metric, CancellationToken ct = default);

    /// <summary>The metric names this computer supports (e.g., "rouge-l", "bleu", "f1").</summary>
    IReadOnlyList<string> SupportedMetrics { get; }
}
