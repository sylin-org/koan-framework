using FluentAssertions;
using Koan.Canon.Domain.Model;
using Xunit;
using System.Linq;

namespace Koan.Canon.Domain.Tests;

public class CanonStageTests
{
    [Fact]
    public void Constructor_ShouldInitializePendingStatus()
    {
        var stage = new CanonStage<TestCanonEntity>();

        stage.Status.Should().Be(CanonStageStatus.Pending);
        stage.Transitions.Should().HaveCount(1);
        stage.Transitions[0].Status.Should().Be(CanonStageStatus.Pending);
    }

    [Fact]
    public void MarkProcessing_ShouldTransitionState()
    {
        var stage = new CanonStage<TestCanonEntity>();

        stage.MarkProcessing("worker", "processing");

        stage.Status.Should().Be(CanonStageStatus.Processing);
        stage.ErrorCode.Should().BeNull();
        stage.Transitions.Last().Status.Should().Be(CanonStageStatus.Processing);
    }

    [Fact]
    public void MarkFailed_ShouldCaptureError()
    {
        var stage = new CanonStage<TestCanonEntity>();
        stage.MarkProcessing();

        stage.MarkFailed("VALIDATION", "Missing key", "worker-1");

        stage.Status.Should().Be(CanonStageStatus.Failed);
        stage.ErrorCode.Should().Be("VALIDATION");
        stage.ErrorMessage.Should().Contain("Missing key");
        stage.Transitions.Last().Notes.Should().Contain("VALIDATION");
    }

    [Fact]
    public void MarkCompleted_ShouldClearErrorsAndRecordSingleCompletionTransition()
    {
        var stage = new CanonStage<TestCanonEntity>();
        stage.MarkProcessing("worker-1", "processing");
        stage.MarkFailed("VALIDATION", "Missing key", "worker-1");

        stage.MarkCompleted("worker-2", "recovered");

        stage.Status.Should().Be(CanonStageStatus.Completed);
        stage.ErrorCode.Should().BeNull();
        stage.ErrorMessage.Should().BeNull();
        stage.Transitions.Last().Status.Should().Be(CanonStageStatus.Completed);

        stage.MarkCompleted("worker-3", "duplicate noop");

        stage.Transitions.Count(transition => transition.Status == CanonStageStatus.Completed).Should().Be(1);
    }

    [Fact]
    public void Park_ShouldRequireReasonAndSetFailureState()
    {
        var stage = new CanonStage<TestCanonEntity>();

        var invalid = () => stage.Park(" ");
        invalid.Should().Throw<ArgumentException>();

        stage.Park("Manual review required", "auditor");

        stage.Status.Should().Be(CanonStageStatus.Parked);
        stage.ErrorCode.Should().Be("parked");
        stage.ErrorMessage.Should().Contain("Manual review");
        stage.Transitions.Last().Status.Should().Be(CanonStageStatus.Parked);
        stage.Transitions.Last().Actor.Should().Be("auditor");
    }

    [Fact]
    public void ResetToPending_ShouldClearErrorsAndAppendTransition()
    {
        var stage = new CanonStage<TestCanonEntity>();
        stage.MarkProcessing("worker-1", "processing");
        stage.MarkFailed("VALIDATION", "Missing key", "worker-1");
        var priorTransitionCount = stage.Transitions.Count;

        stage.ResetToPending("system", "retry");

        stage.Status.Should().Be(CanonStageStatus.Pending);
        stage.ErrorCode.Should().BeNull();
        stage.ErrorMessage.Should().BeNull();
        stage.Transitions.Last().Status.Should().Be(CanonStageStatus.Pending);
        stage.Transitions.Last().Notes.Should().Contain("retry");
        stage.Transitions.Should().HaveCount(priorTransitionCount + 1);
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
    }
}
