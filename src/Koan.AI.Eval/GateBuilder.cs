namespace Koan.AI.Eval;

/// <summary>
/// Fluent builder for quality gate conditions.
/// Builds an immutable list of metric requirements and regression tolerance rules.
/// </summary>
public sealed class GateBuilder : IGateBuilder
{
    private readonly List<GateCondition> _conditions = [];

    /// <inheritdoc />
    public IGateBuilder Metric(string metric, double? min = null, double? max = null, TimeSpan? maxDuration = null)
    {
        _conditions.Add(new GateCondition(metric, min, max, maxDuration));
        return this;
    }

    /// <inheritdoc />
    public IGateBuilder NoRegression(double tolerance = 0.01)
    {
        _conditions.Add(new GateCondition("__no_regression__", Tolerance: tolerance));
        return this;
    }

    internal IReadOnlyList<GateCondition> Build() => _conditions.AsReadOnly();
}

/// <summary>
/// A single gate condition: either a metric threshold or a no-regression rule.
/// </summary>
internal sealed record GateCondition(
    string Metric,
    double? Min = null,
    double? Max = null,
    TimeSpan? MaxDuration = null,
    double? Tolerance = null)
{
    public bool IsNoRegression => Metric == "__no_regression__";
}
