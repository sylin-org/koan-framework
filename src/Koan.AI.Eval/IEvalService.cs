using Koan.AI.Contracts.Shared;

namespace Koan.AI.Eval;

/// <summary>
/// Service interface for model evaluation operations: measurement, gating, comparison, regression, and drift.
/// </summary>
public interface IEvalService
{
    /// <summary>Measure a model against a dataset using the specified metrics.</summary>
    Task<EvalResult> MeasureAsync(
        ModelRef model, DatasetRef data, string[] metrics, CancellationToken ct = default);

    /// <summary>
    /// Evaluate a model against quality gates. Throws <see cref="GateFailedException"/> on failure.
    /// </summary>
    Task<EvalResult> GateAsync(
        ModelRef model, ModelRef? baseline, DatasetRef data,
        Action<IGateBuilder> require, CancellationToken ct = default);

    /// <summary>Compare multiple models on the same dataset and metrics.</summary>
    Task<IReadOnlyList<EvalResult>> CompareAsync(
        ModelRef[] models, DatasetRef data, string[] metrics, CancellationToken ct = default);

    /// <summary>Check whether the current model regresses vs. baseline beyond threshold.</summary>
    Task<EvalResult> RegressAsync(
        ModelRef current, ModelRef baseline, DatasetRef data,
        double threshold = 0.01, CancellationToken ct = default);

    /// <summary>Detect distribution drift between baseline and current evaluation results.</summary>
    Task<DriftResult> DriftAsync(
        EvalResult baseline, EvalResult current, CancellationToken ct = default);

    /// <summary>Run a comprehensive benchmark suite against a model.</summary>
    Task<EvalResult> BenchmarkAsync(
        ModelRef model, DatasetRef data, CancellationToken ct = default);
}
