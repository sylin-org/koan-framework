using Koan.AI.Contracts.Shared;

namespace Koan.AI.Eval;

/// <summary>
/// Placeholder implementation of <see cref="IEvalService"/>.
/// All operations throw <see cref="NotImplementedException"/> until a measurement
/// backend (e.g., Koan.AI.Eval.Local) is registered.
/// </summary>
internal sealed class EvalServiceStub : IEvalService
{
    private const string Message = "Eval operations require a measurement backend. " +
        "Add a Koan.AI.Eval.* extension package to enable model evaluation.";

    public Task<EvalResult> MeasureAsync(ModelRef model, DatasetRef data, string[] metrics, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<EvalResult> GateAsync(ModelRef model, ModelRef? baseline, DatasetRef data, Action<IGateBuilder> require, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<IReadOnlyList<EvalResult>> CompareAsync(ModelRef[] models, DatasetRef data, string[] metrics, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<EvalResult> RegressAsync(ModelRef current, ModelRef baseline, DatasetRef data, double threshold, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<DriftResult> DriftAsync(EvalResult baseline, EvalResult current, CancellationToken ct)
        => throw new NotImplementedException(Message);

    public Task<EvalResult> BenchmarkAsync(ModelRef model, DatasetRef data, CancellationToken ct)
        => throw new NotImplementedException(Message);
}
