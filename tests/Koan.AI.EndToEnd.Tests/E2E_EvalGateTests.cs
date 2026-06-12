using AwesomeAssertions;
using Koan.AI.Contracts.Shared;
using Koan.AI.Eval;
using Koan.Core.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// End-to-end tests verifying Eval.Gate() and Eval.Measure() flow through the
/// full DI-bootstrapped framework: facade -> IEvalService -> AdapterResolver -> adapter.
/// </summary>
[Trait("Category", "EndToEnd")]
[Collection("EndToEnd")]
public sealed class E2E_EvalGateTests : IDisposable
{
    private readonly KoanTestFixture _fixture;

    public E2E_EvalGateTests()
    {
        _fixture = new KoanTestFixture();
    }

    [Fact]
    public async Task Eval_Gate_NoMetricAdapter_ThrowsClearError()
    {
        // Only register a Chat adapter -- no MetricCompute capability.
        _fixture.RegisterAdapter("chat-only", AiCapability.Chat);

        ModelRef model = "my-model";
        var data = new DatasetRef("test-data");

        var act = () => Koan.AI.Eval.Eval.Gate(
            model, baseline: null, data,
            g => g.Metric(Metric.F1, min: 0.5));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No adapter with MetricCompute*");
    }

    [Fact]
    public async Task Eval_Gate_WithMetricAdapter_Passes()
    {
        _fixture.RegisterAdapter("metric-engine", AiCapability.MetricCompute);

        ModelRef model = "my-model";
        var data = new DatasetRef("test-data");

        // ComputeMetricAsync returns 0.0 for all metrics (current impl).
        // Set threshold at or below 0.0 so the gate passes.
        var result = await Koan.AI.Eval.Eval.Gate(
            model, baseline: null, data,
            g => g.Metric(Metric.F1, min: 0.0));

        result.Passed.Should().BeTrue();
        result.Model.Id.Should().Be("my-model");
    }

    [Fact]
    public async Task Eval_Gate_ThresholdTooHigh_ThrowsGateFailedException()
    {
        _fixture.RegisterAdapter("metric-engine", AiCapability.MetricCompute);

        ModelRef model = "test-model";
        var data = new DatasetRef("test-data");

        // ComputeMetricAsync returns 0.0, so min: 0.9 will fail.
        var act = () => Koan.AI.Eval.Eval.Gate(
            model, baseline: null, data,
            g => g.Metric(Metric.F1, min: 0.9));

        var ex = await act.Should().ThrowAsync<GateFailedException>();
        ex.Which.Violations.Should().ContainSingle(v =>
            v.Metric == Metric.F1 && v.Type == GateViolationType.BelowMinimum);
    }

    [Fact]
    public async Task Eval_Measure_ReturnsScoresForAllMetrics()
    {
        _fixture.RegisterAdapter("metric-engine", AiCapability.MetricCompute);

        ModelRef model = "test-model";
        var data = new DatasetRef("test-data");

        var result = await Koan.AI.Eval.Eval.Measure(
            model, data, [Metric.Accuracy, Metric.F1, Metric.Precision]);

        result.Passed.Should().BeTrue();
        result.Scores.Should().HaveCount(3);
        result.Scores.Select(s => s.Metric).Should()
            .Contain(Metric.Accuracy)
            .And.Contain(Metric.F1)
            .And.Contain(Metric.Precision);
    }

    [Fact]
    public async Task Eval_Drift_WithSameScores_ReturnsOk()
    {
        _fixture.RegisterAdapter("metric-engine", AiCapability.MetricCompute);

        var baseline = new EvalResult(
            "baseline", [new EvalScore(Metric.Accuracy, 0.85)], Passed: true);
        var current = new EvalResult(
            "current", [new EvalScore(Metric.Accuracy, 0.85)], Passed: true);

        var drift = await Koan.AI.Eval.Eval.Drift(baseline, current);

        drift.Status.Should().Be(DriftStatus.OK);
        drift.Score.Should().Be(0.0);
    }

    [Fact]
    public void EvalService_IsResolvableFromDI()
    {
        var service = _fixture.Services.GetService<IEvalService>();
        service.Should().NotBeNull("IEvalService should be registered by Koan.AI.Eval auto-registrar");
    }

    public void Dispose() => _fixture.Dispose();
}
