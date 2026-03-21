namespace Koan.AI.Contracts.Shared;

/// <summary>
/// A single metric score from model evaluation.
/// Shared across Eval, Model lineage, and Pipeline gate contexts.
/// </summary>
public sealed record EvalScore(string Metric, double Value, double? Baseline = null)
{
    /// <summary>Improvement over baseline (positive = better).</summary>
    public double? Improvement => Baseline is not null ? Value - Baseline.Value : null;

    public override string ToString() =>
        Baseline is not null
            ? $"{Metric}: {Value:F3} (baseline: {Baseline:F3}, {(Improvement >= 0 ? "+" : "")}{Improvement:F3})"
            : $"{Metric}: {Value:F3}";
}

/// <summary>
/// Complete evaluation result for a model. Carries pass/fail determination.
/// </summary>
public sealed record EvalResult(
    ModelRef Model,
    IReadOnlyList<EvalScore> Scores,
    bool Passed,
    string? Reason = null)
{
    public override string ToString() =>
        $"Eval [{(Passed ? "PASSED" : "FAILED")}] {Model}: {string.Join(", ", Scores)}";
}
