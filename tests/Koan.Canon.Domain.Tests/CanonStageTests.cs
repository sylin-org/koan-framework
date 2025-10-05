using FluentAssertions;
using Koan.Canon.Domain.Model;
using Xunit;

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

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
    }
}
