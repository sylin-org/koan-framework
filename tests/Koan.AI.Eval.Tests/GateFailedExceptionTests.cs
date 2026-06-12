using AwesomeAssertions;
using Koan.AI.Contracts.Shared;
using Koan.AI.Eval;
using Xunit;

namespace Koan.AI.Eval.Tests;

public class GateFailedExceptionTests
{
    [Fact]
    public void Exception_ContainsViolations()
    {
        var model = new ModelRef("acme-support", 3);
        var violations = new List<GateViolation>
        {
            new("rouge_l", 0.72, 0.85, GateViolationType.BelowMinimum),
            new("latency", 250, 200, GateViolationType.AboveMaximum)
        };

        var ex = new GateFailedException(model, null, violations);

        ex.Violations.Should().HaveCount(2);
        ex.Violations[0].Metric.Should().Be("rouge_l");
        ex.Violations[1].Type.Should().Be(GateViolationType.AboveMaximum);
        ex.Model.Should().Be(model);
        ex.Baseline.Should().BeNull();
    }

    [Fact]
    public void Exception_Message_IncludesModelInfo()
    {
        var model = new ModelRef("test-model", 2);
        var violations = new List<GateViolation>
        {
            new("accuracy", 0.6, 0.9, GateViolationType.BelowMinimum)
        };

        var ex = new GateFailedException(model, new ModelRef("baseline-model"), violations);

        ex.Message.Should().Contain("test-model");
        ex.Message.Should().Contain("accuracy");
        ex.Baseline.Should().NotBeNull();
        ex.Baseline!.Id.Should().Be("baseline-model");
    }
}
