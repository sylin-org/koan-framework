namespace Koan.AI.Eval;

/// <summary>
/// Fluent builder for quality gate requirements.
/// Defines minimum/maximum thresholds per metric and regression tolerance.
/// </summary>
public interface IGateBuilder
{
    /// <summary>
    /// Add a metric requirement with optional min, max, and max-duration thresholds.
    /// </summary>
    IGateBuilder Metric(string metric, double? min = null, double? max = null, TimeSpan? maxDuration = null);

    /// <summary>
    /// Require that no metric regresses beyond the given tolerance compared to baseline.
    /// </summary>
    IGateBuilder NoRegression(double tolerance = 0.01);
}
