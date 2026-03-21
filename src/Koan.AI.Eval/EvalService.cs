using Koan.AI.Contracts.Shared;

namespace Koan.AI.Eval;

/// <summary>
/// Real implementation of <see cref="IEvalService"/>.
/// Delegates metric computation to <see cref="IMetricComputer"/> implementations
/// registered via DI, then applies gate logic, comparison, and regression analysis.
/// </summary>
internal sealed class EvalService : IEvalService
{
    private const string NoComputersMessage =
        "No metric computers registered. Implement IMetricComputer and register via DI " +
        "to enable metric evaluation.";

    private readonly IReadOnlyList<IMetricComputer> _computers;

    public EvalService(IEnumerable<IMetricComputer> computers)
    {
        _computers = computers.ToList().AsReadOnly();
    }

    public async Task<EvalResult> MeasureAsync(
        ModelRef model, DatasetRef data, string[] metrics, CancellationToken ct = default)
    {
        EnsureComputersRegistered();

        var scores = new List<EvalScore>();
        foreach (var metric in metrics)
        {
            var value = await ComputeMetricAsync(model, data, metric, ct);
            scores.Add(new EvalScore(metric, value));
        }

        return new EvalResult(model, scores.AsReadOnly(), Passed: true);
    }

    public async Task<EvalResult> GateAsync(
        ModelRef model, ModelRef? baseline, DatasetRef data,
        Action<IGateBuilder> require, CancellationToken ct = default)
    {
        EnsureComputersRegistered();

        var builder = new GateBuilder();
        require(builder);
        var conditions = builder.Build();

        // Collect all metric names referenced by conditions (excluding no-regression sentinel).
        var metricNames = conditions
            .Where(c => !c.IsNoRegression)
            .Select(c => c.Metric)
            .Distinct()
            .ToArray();

        // Measure the model.
        var modelScores = new Dictionary<string, double>();
        foreach (var metric in metricNames)
        {
            modelScores[metric] = await ComputeMetricAsync(model, data, metric, ct);
        }

        // Measure the baseline if needed for no-regression checks.
        Dictionary<string, double>? baselineScores = null;
        var hasNoRegression = conditions.Any(c => c.IsNoRegression);
        if (hasNoRegression && baseline is not null)
        {
            baselineScores = new Dictionary<string, double>();
            foreach (var metric in metricNames)
            {
                baselineScores[metric] = await ComputeMetricAsync(baseline, data, metric, ct);
            }
        }

        // Evaluate conditions.
        var violations = new List<GateViolation>();
        foreach (var condition in conditions)
        {
            if (condition.IsNoRegression)
            {
                if (baseline is null || baselineScores is null)
                    continue;

                var tolerance = condition.Tolerance ?? 0.01;
                foreach (var (metric, modelValue) in modelScores)
                {
                    if (baselineScores.TryGetValue(metric, out var baselineValue))
                    {
                        var regression = baselineValue - modelValue;
                        if (regression > tolerance)
                        {
                            violations.Add(new GateViolation(
                                metric, modelValue, baselineValue, GateViolationType.Regression));
                        }
                    }
                }
            }
            else
            {
                if (!modelScores.TryGetValue(condition.Metric, out var value))
                    continue;

                if (condition.Min is not null && value < condition.Min.Value)
                {
                    violations.Add(new GateViolation(
                        condition.Metric, value, condition.Min.Value, GateViolationType.BelowMinimum));
                }

                if (condition.Max is not null && value > condition.Max.Value)
                {
                    violations.Add(new GateViolation(
                        condition.Metric, value, condition.Max.Value, GateViolationType.AboveMaximum));
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new GateFailedException(model, baseline, violations.AsReadOnly());
        }

        // Build result scores with baseline comparison when available.
        var resultScores = modelScores.Select(kvp =>
        {
            double? baselineValue = baselineScores?.GetValueOrDefault(kvp.Key);
            return new EvalScore(kvp.Key, kvp.Value, baselineValue);
        }).ToList().AsReadOnly();

        return new EvalResult(model, resultScores, Passed: true);
    }

    public async Task<IReadOnlyList<EvalResult>> CompareAsync(
        ModelRef[] models, DatasetRef data, string[] metrics, CancellationToken ct = default)
    {
        EnsureComputersRegistered();

        var results = new List<EvalResult>();
        foreach (var model in models)
        {
            var result = await MeasureAsync(model, data, metrics, ct);
            results.Add(result);
        }

        // Rank by average score descending.
        return results
            .OrderByDescending(r => r.Scores.Average(s => s.Value))
            .ToList()
            .AsReadOnly();
    }

    public async Task<EvalResult> RegressAsync(
        ModelRef current, ModelRef baseline, DatasetRef data,
        double threshold = 0.01, CancellationToken ct = default)
    {
        EnsureComputersRegistered();

        // Use GateAsync with a no-regression condition.
        // If it throws GateFailedException, wrap it in a failed EvalResult.
        try
        {
            return await GateAsync(current, baseline, data,
                g => g.NoRegression(threshold), ct);
        }
        catch (GateFailedException ex)
        {
            var scores = ex.Violations.Select(v =>
                new EvalScore(v.Metric, v.Actual, v.Required)).ToList().AsReadOnly();

            return new EvalResult(current, scores, Passed: false,
                Reason: ex.Message);
        }
    }

    public Task<DriftResult> DriftAsync(
        EvalResult baseline, EvalResult current, CancellationToken ct = default)
    {
        // Compare score distributions between baseline and current evaluations.
        // Drift score is the mean absolute difference across shared metrics.
        var baselineScores = baseline.Scores.ToDictionary(s => s.Metric, s => s.Value);
        var currentScores = current.Scores.ToDictionary(s => s.Metric, s => s.Value);

        var sharedMetrics = baselineScores.Keys.Intersect(currentScores.Keys).ToList();

        if (sharedMetrics.Count == 0)
        {
            return Task.FromResult(new DriftResult
            {
                Score = 0,
                Status = DriftStatus.OK,
                TopShifts = [],
                Recommendation = "No shared metrics between baseline and current — unable to compute drift."
            });
        }

        var shifts = new List<string>();
        var totalDrift = 0.0;

        foreach (var metric in sharedMetrics)
        {
            var diff = Math.Abs(currentScores[metric] - baselineScores[metric]);
            totalDrift += diff;
            if (diff > 0.05)
                shifts.Add($"{metric}: {baselineScores[metric]:F3} → {currentScores[metric]:F3} (Δ{diff:F3})");
        }

        var avgDrift = totalDrift / sharedMetrics.Count;

        var status = avgDrift switch
        {
            < 0.1 => DriftStatus.OK,
            < 0.3 => DriftStatus.Notice,
            _ => DriftStatus.Warning
        };

        var recommendation = status switch
        {
            DriftStatus.Warning => "Significant drift detected. Consider retraining with recent data.",
            DriftStatus.Notice => "Minor drift observed. Monitor closely.",
            _ => null
        };

        return Task.FromResult(new DriftResult
        {
            Score = avgDrift,
            Status = status,
            TopShifts = shifts,
            Recommendation = recommendation
        });
    }

    public async Task<EvalResult> BenchmarkAsync(
        ModelRef model, DatasetRef data, CancellationToken ct = default)
    {
        // Benchmark delegates to MeasureAsync with a standard set of metrics.
        // The actual metric computation depends on registered IMetricComputer instances.
        var standardMetrics = new[]
        {
            Metric.Accuracy, Metric.F1, Metric.Perplexity, Metric.Coherence
        };

        return await MeasureAsync(model, data, standardMetrics, ct);
    }

    // ── Internal Helpers ──

    private void EnsureComputersRegistered()
    {
        if (_computers.Count == 0)
            throw new InvalidOperationException(NoComputersMessage);
    }

    private async Task<double> ComputeMetricAsync(
        ModelRef model, DatasetRef data, string metric, CancellationToken ct)
    {
        var computer = _computers.FirstOrDefault(c =>
            c.SupportedMetrics.Contains(metric, StringComparer.OrdinalIgnoreCase));

        if (computer is null)
        {
            throw new InvalidOperationException(
                $"No metric computer supports '{metric}'. " +
                $"Available metrics: {string.Join(", ", _computers.SelectMany(c => c.SupportedMetrics).Distinct())}");
        }

        return await computer.ComputeAsync(model, data, metric, ct);
    }
}
