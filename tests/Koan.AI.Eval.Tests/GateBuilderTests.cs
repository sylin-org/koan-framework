using AwesomeAssertions;
using Koan.AI.Eval;
using Xunit;

namespace Koan.AI.Eval.Tests;

public class GateBuilderTests
{
    [Fact]
    public void Metric_AddsCondition()
    {
        var builder = new GateBuilder();

        builder.Metric("rouge_l", min: 0.85);

        var conditions = builder.Build();
        conditions.Should().HaveCount(1);
        conditions[0].Metric.Should().Be("rouge_l");
        conditions[0].Min.Should().Be(0.85);
    }

    [Fact]
    public void Metric_WithMinAndMax()
    {
        var builder = new GateBuilder();

        builder.Metric("latency", max: 100);

        var conditions = builder.Build();
        conditions.Should().HaveCount(1);
        conditions[0].Max.Should().Be(100);
        conditions[0].Min.Should().BeNull();
    }

    [Fact]
    public void NoRegression_AddsSpecialCondition()
    {
        var builder = new GateBuilder();

        builder.NoRegression(0.02);

        var conditions = builder.Build();
        conditions.Should().HaveCount(1);
        conditions[0].IsNoRegression.Should().BeTrue();
        conditions[0].Tolerance.Should().Be(0.02);
    }

    [Fact]
    public void MultipleConditions_BuildsAll()
    {
        var builder = new GateBuilder();

        builder.Metric("rouge_l", min: 0.8);
        builder.Metric("latency", max: 200);
        builder.NoRegression(0.01);

        var conditions = builder.Build();
        conditions.Should().HaveCount(3);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        var builder = new GateBuilder();

        var result = builder
            .Metric("rouge_l", min: 0.8)
            .Metric("latency", max: 200)
            .NoRegression();

        result.Should().BeSameAs(builder);
        builder.Build().Should().HaveCount(3);
    }
}
