using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Shared;
using Koan.AI.Eval;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Eval;

public sealed class EvalServiceSpec
{
    private const string NoAdapterMessage =
        "No adapter with MetricCompute capability registered. " +
        "Add an adapter that declares AiCapability.MetricCompute to enable evaluation.";

    [Fact]
    public async Task Measure_without_metric_capable_adapter_fails_with_correction()
    {
        var service = CreateService();

        var act = () => service.Measure("support-model", new DatasetRef("support-regression"), [Metric.Accuracy]);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be(NoAdapterMessage);
    }

    [Fact]
    public async Task Measure_with_capability_declared_but_interface_missing_names_both_remedy_parts()
    {
        var registry = new InMemoryAdapterRegistry();
        registry.Compile([new FlagOnlyAdapter()]);
        var service = new EvalService(registry);

        var act = () => service.Measure("support-model", new DatasetRef("support-regression"), [Metric.Accuracy]);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("'flag-only'").And
            .Contain("does not implement IMetricAdapter");
    }

    [Fact]
    public async Task Measure_delegates_to_implementing_adapter_and_carries_its_scores()
    {
        var service = CreateService(new ComputingAdapter());

        var result = await service.Measure(
            new ModelRef("support-model", Version: 4),
            new DatasetRef("support-regression", Hash: "abc123"),
            [Metric.Accuracy, Metric.F1]);

        result.Passed.Should().BeTrue();
        result.Scores.Should().HaveCount(2);
        result.Scores.Single(s => s.Metric == Metric.Accuracy).Value.Should().Be(0.93);
        result.Scores.Single(s => s.Metric == Metric.F1).Value.Should().Be(0.88);
    }

    [Fact]
    public async Task Gate_throws_with_violation_when_score_is_below_minimum()
    {
        var service = CreateService(new ComputingAdapter());

        var act = () => service.Gate(
            "candidate", baseline: null, new DatasetRef("support-regression"),
            g => g.Metric(Metric.Accuracy, min: 0.95));

        var ex = await act.Should().ThrowAsync<GateFailedException>();
        ex.Which.Violations.Should().ContainSingle();
        var violation = ex.Which.Violations[0];
        violation.Type.Should().Be(GateViolationType.BelowMinimum);
        violation.Metric.Should().Be(Metric.Accuracy);
        violation.Actual.Should().Be(0.93);
        violation.Required.Should().Be(0.95);
    }

    [Fact]
    public async Task Gate_passes_when_all_conditions_hold()
    {
        var service = CreateService(new ComputingAdapter());

        var result = await service.Gate(
            "candidate", baseline: null, new DatasetRef("support-regression"),
            g => g.Metric(Metric.Accuracy, min: 0.90).Metric(Metric.F1, min: 0.85));

        result.Passed.Should().BeTrue();
        result.Scores.Single(s => s.Metric == Metric.Accuracy).Value.Should().Be(0.93);
    }

    [Fact]
    public async Task Gate_flags_regression_beyond_tolerance_against_baseline()
    {
        // One adapter serves both sides: candidate computes 0.93, baseline 0.95 - a regression
        // of 0.02 that exceeds the 0.01 tolerance.
        var service = CreateService(new ComputingAdapter());

        var act = () => service.Gate(
            "candidate", baseline: "baseline", new DatasetRef("support-regression"),
            g => g.Metric(Metric.Accuracy).NoRegression(tolerance: 0.01));

        var ex = await act.Should().ThrowAsync<GateFailedException>();
        var violation = ex.Which.Violations.Should().ContainSingle().Subject;
        violation.Type.Should().Be(GateViolationType.Regression);
        violation.Actual.Should().Be(0.93);
        violation.Required.Should().Be(0.95);
    }

    [Fact]
    public async Task Gate_with_standalone_no_regression_refuses_to_pass_vacuously()
    {
        var service = CreateService(new ComputingAdapter());

        var act = () => service.Gate(
            "candidate", baseline: "baseline", new DatasetRef("support-regression"),
            g => g.NoRegression(tolerance: 0.01));

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("requires at least one Metric(...)");
    }

    [Fact]
    public async Task Gate_with_no_regression_but_no_baseline_refuses_to_skip_the_check()
    {
        var service = CreateService(new ComputingAdapter());

        var act = () => service.Gate(
            "candidate", baseline: null, new DatasetRef("support-regression"),
            g => g.Metric(Metric.Accuracy, min: 0.90).NoRegression(tolerance: 0.01));

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("requires a baseline model");
    }

    [Fact]
    public async Task Drift_reports_ok_for_small_shared_metric_movement()
    {
        var service = CreateService();
        var baseline = new EvalResult("v3", [new EvalScore(Metric.Accuracy, 0.93)], Passed: true);
        var current = new EvalResult("v4", [new EvalScore(Metric.Accuracy, 0.90)], Passed: true);

        var drift = await service.Drift(baseline, current);

        drift.Status.Should().Be(DriftStatus.OK);
        drift.Score.Should().BeApproximately(0.03, 1e-9);
        drift.TopShifts.Should().BeEmpty();
    }

    [Fact]
    public async Task Drift_raises_notice_and_lists_shifts_for_large_movement()
    {
        var service = CreateService();
        var baseline = new EvalResult("v3", [new EvalScore(Metric.Accuracy, 0.95)], Passed: true);
        var current = new EvalResult("v4", [new EvalScore(Metric.Accuracy, 0.80)], Passed: true);

        var drift = await service.Drift(baseline, current);

        drift.Status.Should().Be(DriftStatus.Notice);
        drift.Score.Should().BeApproximately(0.15, 1e-9);
        drift.TopShifts.Should().ContainSingle().Which.Should().Contain("accuracy");
    }

    [Fact]
    public async Task Drift_without_shared_metrics_answers_explicitly()
    {
        var service = CreateService();
        var baseline = new EvalResult("v3", [new EvalScore(Metric.Perplexity, 12.0)], Passed: true);
        var current = new EvalResult("v4", [new EvalScore(Metric.Accuracy, 0.90)], Passed: true);

        var drift = await service.Drift(baseline, current);

        drift.Status.Should().Be(DriftStatus.OK);
        drift.Recommendation.Should().Contain("No shared metrics");
    }

    private static EvalService CreateService(params IAiAdapter[] adapters)
    {
        var registry = new InMemoryAdapterRegistry();
        if (adapters.Length > 0) registry.Compile(adapters);
        return new EvalService(registry);
    }

    /// <summary>Declares the capability flag but not the structural interface - a lie under test.</summary>
    private sealed class FlagOnlyAdapter : IAiAdapter
    {
        public string Id => "flag-only";
        public string Name => "Flag-only adapter";
        public string Type => "fake";
        public IReadOnlySet<string> Capabilities { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AiCapability.MetricCompute };
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

    private sealed class ComputingAdapter : IAiAdapter, IMetricAdapter
    {
        public string Id => "metric:computing";
        public string Name => "Computing metric adapter";
        public string Type => "fake";
        public IReadOnlySet<string> Capabilities { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AiCapability.MetricCompute };
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
        public Task<double> ComputeMetricAsync(ModelRef model, DatasetRef data, string metric, CancellationToken cancellationToken = default)
        {
            var baseValue = model.Id switch
            {
                "baseline" => 0.95, // one notch above the candidate, to stand in as the regression baseline
                _ => 0.93
            };

            return Task.FromResult(metric switch
            {
                Metric.Accuracy => baseValue,
                Metric.F1 => baseValue - 0.05,
                _ => throw new InvalidOperationException($"Metric '{metric}' is not computable by this adapter.")
            });
        }
    }
}
