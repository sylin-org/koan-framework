using FluentAssertions;
using Koan.AI.Contracts.Shared;
using Xunit;

namespace Koan.AI.Contracts.Shared.Tests;

public class SharedModelsTests
{
    [Fact]
    public void ModelRef_ImplicitFromString()
    {
        ModelRef r = "llama3.1";

        r.Id.Should().Be("llama3.1");
        r.Version.Should().BeNull();
    }

    [Fact]
    public void ModelRef_ToString_WithVersion()
    {
        var r = new ModelRef("x", 3);

        r.ToString().Should().Be("x:v3");
    }

    [Fact]
    public void ModelRef_ToString_WithoutVersion()
    {
        var r = new ModelRef("x");

        r.ToString().Should().Be("x");
    }

    [Fact]
    public void EvalScore_Improvement_Calculated()
    {
        var score = new EvalScore("accuracy", 0.92, Baseline: 0.85);

        score.Improvement.Should().BeApproximately(0.07, 0.0001);
    }

    [Fact]
    public void EvalScore_NoBaseline_ImprovementNull()
    {
        var score = new EvalScore("accuracy", 0.92);

        score.Improvement.Should().BeNull();
    }

    [Fact]
    public void ComputeRequirement_Default_IsAny()
    {
        var req = ComputeRequirement.Default;

        req.Accelerator.Should().Be(Accelerator.Any);
        req.MinVramBytes.Should().BeNull();
        req.Location.Should().BeNull();
        req.PreferredNode.Should().BeNull();
    }

    [Fact]
    public void ComputeRequirement_WithVram_ConvertsGiB()
    {
        var req = ComputeRequirement.WithVram(8);

        req.MinVramBytes.Should().Be(8L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void JobRef_ToString()
    {
        var job = new JobRef("job-123", JobStatus.Running);

        job.ToString().Should().Be("Job job-123 [Running]");
    }

    [Fact]
    public void Lineage_ToString_IncludesFields()
    {
        var lineage = new Lineage(
            Base: new ModelRef("llama-3.1", 1),
            Method: "LoRA");

        var str = lineage.ToString();

        str.Should().Contain("base=");
        str.Should().Contain("llama-3.1");
        str.Should().Contain("method=LoRA");
    }
}
