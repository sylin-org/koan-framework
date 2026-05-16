using Koan.AI.Contracts.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Eval;

/// <summary>
/// Static facade for Koan model evaluation. Measure quality, enforce gates,
/// compare models, and detect drift — all without injecting services.
///
/// <code>
/// var result = await Eval.Measure("my-model", "test-set", [Metric.F1, Metric.Accuracy]);
/// await Eval.Gate("my-model", "baseline", "test-set", g => g.Metric(Metric.F1, min: 0.9));
/// var drift = await Eval.Drift(baseline, current);
/// </code>
/// </summary>
public static class Eval
{
    // ── Measurement ──

    /// <summary>Measure a model against a dataset using the specified metrics.</summary>
    public static async Task<EvalResult> Measure(
        ModelRef model, DatasetRef data, string[] metrics, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Measure(model, data, metrics, ct);
    }

    // ── Quality Gates ──

    /// <summary>
    /// Evaluate a model against quality gates. Throws <see cref="GateFailedException"/> on failure.
    /// </summary>
    public static async Task<EvalResult> Gate(
        ModelRef model, ModelRef? baseline, DatasetRef data,
        Action<IGateBuilder> require, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Gate(model, baseline, data, require, ct);
    }

    // ── Comparison ──

    /// <summary>Compare multiple models on the same dataset and metrics.</summary>
    public static async Task<IReadOnlyList<EvalResult>> Compare(
        ModelRef[] models, DatasetRef data, string[] metrics, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Compare(models, data, metrics, ct);
    }

    // ── Regression ──

    /// <summary>Check whether the current model regresses vs. baseline beyond threshold.</summary>
    public static async Task<EvalResult> Regress(
        ModelRef current, ModelRef baseline, DatasetRef data,
        double threshold = 0.01, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Regress(current, baseline, data, threshold, ct);
    }

    // ── Drift ──

    /// <summary>Detect distribution drift between baseline and current evaluation results.</summary>
    public static async Task<DriftResult> Drift(
        EvalResult baseline, EvalResult current, CancellationToken ct = default)
    {
        var service = ResolveService();
        return await service.Drift(baseline, current, ct);
    }

    // ── Internal ──

    private static IEvalService ResolveService()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Eval service not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Eval.*");

        return provider.GetRequiredService<IEvalService>();
    }
}
