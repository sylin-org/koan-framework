using Koan.AI.Contracts.Shared;

namespace Koan.AI.Eval;

/// <summary>
/// Type of gate violation.
/// </summary>
public enum GateViolationType
{
    BelowMinimum,
    AboveMaximum,
    Regression
}

/// <summary>
/// A single quality-gate violation: which metric failed, the actual vs required value, and the type.
/// </summary>
public sealed record GateViolation(
    string Metric,
    double Actual,
    double Required,
    GateViolationType Type);

/// <summary>
/// Thrown when a model fails to pass quality gates during evaluation.
/// </summary>
public sealed class GateFailedException : Exception
{
    public ModelRef Model { get; }
    public ModelRef? Baseline { get; }
    public IReadOnlyList<GateViolation> Violations { get; }

    public GateFailedException(
        ModelRef model,
        ModelRef? baseline,
        IReadOnlyList<GateViolation> violations)
        : base(FormatMessage(model, violations))
    {
        Model = model;
        Baseline = baseline;
        Violations = violations;
    }

    private static string FormatMessage(ModelRef model, IReadOnlyList<GateViolation> violations)
    {
        var details = string.Join("; ", violations.Select(v =>
            $"{v.Metric}: {v.Actual:F3} {v.Type} {v.Required:F3}"));
        return $"Model {model} failed quality gate: {details}";
    }
}
